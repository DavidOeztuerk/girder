using Girder.Abstractions.Security.Passwords;

namespace Girder.Infrastructure.Security.Passwords;

/// <summary>
/// Reads every format a deployment has written, writes only the current one.
/// </summary>
/// <remarks>
/// This is what makes the algorithm a choice rather than a decision taken once.
/// A system arriving from elsewhere brings bcrypt entries; a customer asks for
/// Argon2id; a cost parameter is raised. In each case the old entries stay
/// readable, every successful sign-in reports
/// <see cref="PasswordVerification.SuccessRehashNeeded"/>, and the application
/// rewrites the entry from the password it was just handed — the one moment it
/// exists. Nobody is asked to reset anything.
/// <para>
/// Girder never picks the primary. Which algorithm a deployment writes with is
/// its own decision, exactly like which server its data lives on.
/// </para>
/// </remarks>
public sealed class PasswordHasherChain : IPasswordHasher
{
    private readonly IPasswordHasher _primary;
    private readonly IReadOnlyList<IPasswordHasher> _readers;

    /// <param name="primary">Writes every new entry.</param>
    /// <param name="readers">
    /// Formats that may still be in the store. Order decides who answers when
    /// two recognise the same entry.
    /// </param>
    public PasswordHasherChain(IPasswordHasher primary, IEnumerable<IPasswordHasher> readers)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(readers);

        _primary = primary;
        _readers = [primary, .. readers];
    }

    /// <inheritdoc />
    public string Hash(string password) => _primary.Hash(password);

    /// <inheritdoc />
    public bool CanRead(string encodedHash) => _readers.Any(r => r.CanRead(encodedHash));

    /// <inheritdoc />
    public PasswordVerification Verify(string password, string? encodedHash)
    {
        var reader = encodedHash is null
            ? null
            : _readers.FirstOrDefault(r => r.CanRead(encodedHash));

        if (reader is null)
        {
            // No account, or an entry in a format nobody here knows. The
            // primary is still asked, with nothing to compare against, so the
            // work is spent either way — otherwise how long an answer takes
            // says which formats this deployment understands.
            return _primary.Verify(password, null);
        }

        var verification = reader.Verify(password, encodedHash);

        // Read by anything but the primary means the entry is not in the format
        // this deployment writes, whatever the reason.
        return verification == PasswordVerification.Success && !ReferenceEquals(reader, _primary)
            ? PasswordVerification.SuccessRehashNeeded
            : verification;
    }
}
