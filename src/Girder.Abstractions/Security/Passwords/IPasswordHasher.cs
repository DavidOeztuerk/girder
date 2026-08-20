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
    /// Whether this implementation recognises the format of
    /// <paramref name="encodedHash"/>.
    /// </summary>
    /// <remarks>
    /// Asked by <c>PasswordHasherChain</c>, which reads every format a
    /// deployment has ever written while writing only the current one. That is
    /// what lets the algorithm change — a system arriving with bcrypt entries,
    /// a customer asking for Argon2id, a raised cost — without anyone being
    /// told to reset a password.
    /// <para>
    /// Defaults to <c>false</c> so that adding this to the interface breaks
    /// nothing. An implementation that should take part in a chain has to say
    /// which entries are its own; one used on its own never gets asked.
    /// </para>
    /// </remarks>
    bool CanRead(string encodedHash) => false;

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

/// <summary>
/// A format that may still be in the store but is no longer written.
/// </summary>
/// <remarks>
/// Register one per format a deployment is migrating away from. They join the
/// chain in front of the primary, so an entry written years ago still verifies
/// and the person is moved across on that sign-in without noticing.
/// </remarks>
public interface IPasswordHashReader : IPasswordHasher;

/// <summary>Where the algorithm a deployment writes with is registered.</summary>
public static class PasswordHashing
{
    /// <summary>
    /// The key the writing algorithm is registered under.
    /// </summary>
    /// <remarks>
    /// Keyed, so that what an application injects — <see cref="IPasswordHasher"/>
    /// — is the chain over every registered format, while the one algorithm
    /// that writes stays separately addressable. A provider package registers
    /// itself here to become the writer.
    /// </remarks>
    public const string PrimaryKey = "girder.passwords.primary";
}

