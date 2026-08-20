namespace Girder.Abstractions.Security.Passwords;

/// <summary>What a stored entry says about a presented password.</summary>
public enum PasswordVerification
{
    /// <summary>Wrong password, unreadable entry, or no entry at all.</summary>
    Failed = 0,

    /// <summary>Correct, and stored at the current cost.</summary>
    Success,

    /// <summary>
    /// Correct, but stored with weaker parameters than are configured now.
    /// Rewrite the entry from the password you were just given — it is the only
    /// moment it is available.
    /// </summary>
    SuccessRehashNeeded
}

/// <summary>
/// Turns a password into a stored verifier and checks one against it.
/// </summary>
/// <remarks>
/// A port, because the cost parameters are an operational decision and the
/// algorithm is one a deployment may have to keep: a system arriving from
/// elsewhere brings entries in its old format, and being able to read them is
/// what makes leaving that system possible.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>Produces the entry to store. Never the password.</summary>
    string Hash(string password);

    /// <summary>
    /// Checks <paramref name="password"/> against a stored entry.
    /// </summary>
    /// <param name="password">What the caller presented.</param>
    /// <param name="encodedHash">
    /// The stored entry, or <c>null</c> when there is no account. Null is a
    /// supported input on purpose: the implementation then does the same work
    /// as a real check before answering <see cref="PasswordVerification.Failed"/>.
    /// A separate "dummy" method the caller had to remember is a call that
    /// eventually is not made, and then the time an answer takes says whether
    /// the account exists.
    /// </param>
    /// <returns>
    /// Never throws for a malformed or foreign entry — a store can hold
    /// anything after a migration, and one unreadable row must not break
    /// signing in for everyone.
    /// </returns>
    PasswordVerification Verify(string password, string? encodedHash);
}
