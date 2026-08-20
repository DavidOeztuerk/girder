using Girder.Abstractions.Security.Passwords;

// The namespace of this package shadows the library's own, so the
// alias names the one that does the work.
using Bcrypt = global::BCrypt.Net.BCrypt;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Passwords.BCrypt;

/// <summary>
/// bcrypt, for entries a system already has or a deployment still wants.
/// </summary>
/// <remarks>
/// Its own package because it needs one: <c>BCrypt.Net-Next</c>, MIT. Girder
/// picks no algorithm, exactly as it picks no database — an application that
/// wants bcrypt installs this and says so.
/// <para>
/// The usual reason to add it is a migration: register it as a reader beside
/// whatever writes new entries, and every sign-in moves one person across.
/// </para>
/// </remarks>
public class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    /// <param name="workFactor">
    /// Doubling the cost each step. Twelve is the common floor at the time of
    /// writing; raising it later leaves existing entries readable, because each
    /// carries the factor it was written with.
    /// </param>
    public BCryptPasswordHasher(int workFactor = 12)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workFactor, 4);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(workFactor, 31);

        _workFactor = workFactor;
    }

    /// <inheritdoc />
    public string Hash(string password) =>
        Bcrypt.HashPassword(password, _workFactor);

    /// <inheritdoc />
    /// <remarks>
    /// The three prefixes bcrypt has used. <c>$2y$</c> and <c>$2b$</c> are the
    /// modern ones; <c>$2a$</c> appears in anything old enough to be worth
    /// migrating, which is the case this exists for.
    /// </remarks>
    public bool CanRead(string encodedHash) =>
        encodedHash.StartsWith("$2a$", StringComparison.Ordinal)
        || encodedHash.StartsWith("$2b$", StringComparison.Ordinal)
        || encodedHash.StartsWith("$2y$", StringComparison.Ordinal);

    /// <inheritdoc />
    public PasswordVerification Verify(string password, string? encodedHash)
    {
        if (encodedHash is null || !CanRead(encodedHash))
        {
            // Spend the work anyway: an answer that comes back faster for an
            // unknown account says the account is unknown.
            Bcrypt.HashPassword(password, _workFactor);
            return PasswordVerification.Failed;
        }

        try
        {
            if (!Bcrypt.Verify(password, encodedHash))
            {
                return PasswordVerification.Failed;
            }
        }
        catch (global::BCrypt.Net.SaltParseException)
        {
            // A damaged entry is not a crash. One bad row must not break
            // signing in for everyone.
            return PasswordVerification.Failed;
        }

        return WorkFactorOf(encodedHash) < _workFactor
            ? PasswordVerification.SuccessRehashNeeded
            : PasswordVerification.Success;
    }

    private static int WorkFactorOf(string encodedHash)
    {
        // $2b$12$…  — the factor is the third field.
        var parts = encodedHash.Split('$');
        return parts.Length > 2 && int.TryParse(parts[2], out var factor) ? factor : 0;
    }
}

/// <summary>bcrypt as a format that is read but no longer written.</summary>
public sealed class BCryptPasswordReader(int workFactor = 12)
    : BCryptPasswordHasher(workFactor), IPasswordHashReader;

public static class BCryptPasswordRegistration
{
    /// <summary>
    /// Writes and reads bcrypt entries.
    /// </summary>
    public static IServiceCollection AddBCryptPasswords(
        this IServiceCollection services,
        int workFactor = 12) =>
        services.AddKeyedSingleton<IPasswordHasher>(
            PasswordHashing.PrimaryKey, new BCryptPasswordHasher(workFactor));

    /// <summary>
    /// Reads bcrypt entries without writing any.
    /// </summary>
    /// <remarks>
    /// What a migration registers: entries already in the store stay readable,
    /// new ones are written by whatever the deployment chose, and each sign-in
    /// moves one person across.
    /// </remarks>
    public static IServiceCollection AddBCryptPasswordReader(
        this IServiceCollection services,
        int workFactor = 12) =>
        services.AddSingleton<IPasswordHashReader>(new BCryptPasswordReader(workFactor));
}
