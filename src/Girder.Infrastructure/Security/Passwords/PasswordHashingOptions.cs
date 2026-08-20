namespace Girder.Infrastructure.Security.Passwords;

/// <summary>Cost parameters for <see cref="Pbkdf2PasswordHasher"/>.</summary>
public sealed class PasswordHashingOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "PasswordHashing";

    /// <summary>
    /// PBKDF2 iterations for new entries. OWASP's floor for HMAC-SHA256 at the
    /// time of writing.
    /// </summary>
    /// <remarks>
    /// Raising it does not invalidate what is already stored: every entry
    /// carries the count it was written with, and verifying one below this
    /// value reports <see cref="Girder.Abstractions.Security.Passwords.PasswordVerification.SuccessRehashNeeded"/>.
    /// </remarks>
    public int Iterations { get; set; } = 600_000;

    /// <summary>Salt length in bytes.</summary>
    public int SaltBytes { get; set; } = 16;

    /// <summary>Derived key length in bytes.</summary>
    public int HashBytes { get; set; } = 32;
}
