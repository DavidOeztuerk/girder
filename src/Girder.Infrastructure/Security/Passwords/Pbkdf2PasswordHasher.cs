using System.Security.Cryptography;
using Girder.Abstractions.Security.Passwords;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Security.Passwords;

/// <summary>
/// PBKDF2-HMAC-SHA256, from the runtime's own cryptography.
/// </summary>
/// <remarks>
/// Chosen as the default because it needs nothing installed and nothing
/// licensed: the platform ships it. Argon2id resists custom hardware better and
/// is the better choice where that threat is real — plug it in behind
/// <see cref="IPasswordHasher"/>; it is a dependency decision, not a default.
/// <para>
/// Entries are PHC-shaped — <c>$id$params$salt$hash</c> — so they say what they
/// are. That is what lets the cost be raised later without orphaning what is
/// already stored, and what lets a reader for a different algorithm be layered
/// in front when a system arrives from elsewhere.
/// </para>
/// </remarks>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Identifier = "pbkdf2-sha256";

    private readonly PasswordHashingOptions _options;

    public Pbkdf2PasswordHasher() : this(new PasswordHashingOptions()) { }

    public Pbkdf2PasswordHasher(PasswordHashingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.SaltBytes, 8);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.HashBytes, 16);

        _options = options;
    }

    public Pbkdf2PasswordHasher(IOptions<PasswordHashingOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options))) { }

    /// <inheritdoc />
    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var salt = RandomNumberGenerator.GetBytes(_options.SaltBytes);
        var hash = Derive(password, salt, _options.Iterations, _options.HashBytes);

        return $"${Identifier}$i={_options.Iterations}${ToB64(salt)}${ToB64(hash)}";
    }

    /// <inheritdoc />
    public PasswordVerification Verify(string password, string? encodedHash)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (!TryParse(encodedHash, out var iterations, out var salt, out var expected))
        {
            // No account, or an entry this hasher cannot read. Either way the
            // work happens, so the duration says nothing about which it was.
            BurnEquivalentWork(password);
            return PasswordVerification.Failed;
        }

        var actual = Derive(password, salt, iterations, expected.Length);

        // Fixed-time: an early exit would leak how many bytes matched, and with
        // enough attempts that is the hash.
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerification.Failed;
        }

        return iterations < _options.Iterations
            ? PasswordVerification.SuccessRehashNeeded
            : PasswordVerification.Success;
    }

    /// <summary>
    /// Spends the same work a real check would, so the answer to an unknown
    /// address takes as long as the answer to a wrong password.
    /// </summary>
    /// <remarks>
    /// Closes the hash channel, not every channel: looking the account up in a
    /// store still takes a different amount of time depending on whether it was
    /// found. What this removes is the large, reliable signal — hundreds of
    /// thousands of iterations against none.
    /// <para>
    /// It uses the currently configured cost. While a rehash rollout is under
    /// way, entries written at the old cost verify faster than this — the
    /// guarantee narrows for the length of that rollout, which is a reason to
    /// finish one rather than leave it running.
    /// </para>
    /// </remarks>
    private void BurnEquivalentWork(string password)
    {
        var salt = new byte[_options.SaltBytes];
        var result = Derive(password, salt, _options.Iterations, _options.HashBytes);

        // Consumed so the derivation cannot be optimised away.
        CryptographicOperations.ZeroMemory(result);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);

    /// <summary>
    /// Reads a PHC-shaped entry this hasher wrote. Anything else is refused
    /// rather than rejected loudly, so another reader can be tried.
    /// </summary>
    private static bool TryParse(
        string? encoded,
        out int iterations,
        out byte[] salt,
        out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];

        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        // "" / id / params / salt / hash — the leading $ produces an empty first field.
        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0].Length != 0 || parts[1] != Identifier)
        {
            return false;
        }

        if (!parts[2].StartsWith("i=", StringComparison.Ordinal)
            || !int.TryParse(parts[2][2..], out iterations)
            || iterations < 1)
        {
            return false;
        }

        return TryFromB64(parts[3], out salt) && TryFromB64(parts[4], out hash) && hash.Length >= 16;
    }

    /// <summary>Base64 without padding, as the PHC string format uses it.</summary>
    private static string ToB64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static bool TryFromB64(string value, out byte[] bytes)
    {
        bytes = [];

        if (value.Length == 0)
        {
            return false;
        }

        var padded = value.PadRight(value.Length + ((4 - (value.Length % 4)) % 4), '=');

        try
        {
            bytes = Convert.FromBase64String(padded);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
