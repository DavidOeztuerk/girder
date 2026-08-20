using System.Security.Cryptography;
using System.Text;
using Girder.Abstractions.Security.Passwords;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Passwords.Argon2;

/// <summary>How much work an Argon2id entry costs to produce.</summary>
/// <param name="MemoryKib">
/// Memory per attempt. This is the parameter that matters: it is what makes a
/// graphics card or a purpose-built chip no faster than a server.
/// </param>
/// <param name="Iterations">Passes over that memory.</param>
/// <param name="Parallelism">Lanes. Match it to the cores you are willing to spend.</param>
public readonly record struct Argon2Cost(int MemoryKib, int Iterations, int Parallelism)
{
    /// <summary>
    /// OWASP's recommendation at the time of writing: 19 MiB, two passes, one
    /// lane.
    /// </summary>
    /// <remarks>
    /// A named value rather than default parameters: on a record struct
    /// <c>new Argon2Cost()</c> zeroes every field and does not apply them, so a
    /// fallback written that way silently produces a cost of nothing.
    /// </remarks>
    public static Argon2Cost Recommended => new(19_456, 2, 1);
}

/// <summary>
/// Argon2id — OWASP's first choice, and the reason is memory.
/// </summary>
/// <remarks>
/// PBKDF2 costs an attacker time, which specialised hardware buys back cheaply.
/// Argon2id costs memory, which it does not. Girder's default is PBKDF2 only
/// because it needs no package; where the threat includes someone building
/// hardware for the purpose, this is the better answer and costs one
/// registration.
/// <para>
/// Entries are written in the PHC string format the Argon2 reference
/// implementation uses, so a system that leaves Girder takes them with it and
/// one arriving can be read.
/// </para>
/// </remarks>
public class Argon2PasswordHasher : IPasswordHasher
{
    private const string Identifier = "argon2id";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Version = 19;

    private readonly Argon2Cost _cost;

    /// <param name="cost">
    /// Omit for <see cref="Argon2Cost.Recommended"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Any parameter is not positive.</exception>
    public Argon2PasswordHasher(Argon2Cost cost = default)
    {
        _cost = cost == default ? Argon2Cost.Recommended : cost;

        ArgumentOutOfRangeException.ThrowIfLessThan(_cost.MemoryKib, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(_cost.Iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_cost.Parallelism, 1);
    }

    /// <inheritdoc />
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, _cost);

        return $"${Identifier}$v={Version}"
            + $"$m={_cost.MemoryKib},t={_cost.Iterations},p={_cost.Parallelism}"
            + $"${ToB64(salt)}${ToB64(hash)}";
    }

    /// <inheritdoc />
    public bool CanRead(string encodedHash) =>
        encodedHash.StartsWith($"${Identifier}$", StringComparison.Ordinal);

    /// <inheritdoc />
    public PasswordVerification Verify(string password, string? encodedHash)
    {
        if (encodedHash is null || !TryParse(encodedHash, out var cost, out var salt, out var expected))
        {
            // The work is spent either way, or how long an answer takes says
            // whether the account exists.
            Derive(password, new byte[SaltBytes], _cost);
            return PasswordVerification.Failed;
        }

        var actual = Derive(password, salt, cost);

        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        return cost.MemoryKib < _cost.MemoryKib || cost.Iterations < _cost.Iterations
            ? PasswordVerification.SuccessRehashNeeded
            : PasswordVerification.Success;
    }

    private static byte[] Derive(string password, byte[] salt, Argon2Cost cost)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = cost.MemoryKib,
            Iterations = cost.Iterations,
            DegreeOfParallelism = cost.Parallelism
        };

        return argon.GetBytes(HashBytes);
    }

    /// <summary>
    /// Reads <c>$argon2id$v=19$m=…,t=…,p=…$salt$hash</c>. Anything else is
    /// refused rather than thrown over, so another reader can be tried.
    /// </summary>
    private static bool TryParse(string encoded, out Argon2Cost cost, out byte[] salt, out byte[] hash)
    {
        cost = default;
        salt = [];
        hash = [];

        var parts = encoded.Split('$');
        if (parts.Length != 6 || parts[1] != Identifier || !parts[2].StartsWith("v=", StringComparison.Ordinal))
        {
            return false;
        }

        var parameters = parts[3].Split(',');
        if (parameters.Length != 3
            || !TryRead(parameters[0], "m=", out var memory)
            || !TryRead(parameters[1], "t=", out var iterations)
            || !TryRead(parameters[2], "p=", out var parallelism))
        {
            return false;
        }

        cost = new Argon2Cost(memory, iterations, parallelism);
        return TryFromB64(parts[4], out salt) && TryFromB64(parts[5], out hash);
    }

    private static bool TryRead(string field, string prefix, out int value)
    {
        value = 0;
        return field.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(field[prefix.Length..], out value)
            && value > 0;
    }

    private static string ToB64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryFromB64(string value, out byte[] bytes)
    {
        bytes = [];

        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(
                value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '='));
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Argon2id as a format that is read but no longer written.</summary>
public sealed class Argon2PasswordReader(Argon2Cost cost = default)
    : Argon2PasswordHasher(cost), IPasswordHashReader;

public static class Argon2PasswordRegistration
{
    /// <summary>Writes and reads Argon2id entries.</summary>
    public static IServiceCollection AddArgon2Passwords(
        this IServiceCollection services,
        Argon2Cost cost = default) =>
        services.AddKeyedSingleton<IPasswordHasher>(
            PasswordHashing.PrimaryKey, new Argon2PasswordHasher(cost));

    /// <summary>Reads Argon2id entries without writing any.</summary>
    public static IServiceCollection AddArgon2PasswordReader(
        this IServiceCollection services,
        Argon2Cost cost = default) =>
        services.AddSingleton<IPasswordHashReader>(new Argon2PasswordReader(cost));
}
