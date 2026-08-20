using Girder.Abstractions.Security.Passwords;
using Girder.Infrastructure.Security.Passwords;

namespace Girder.Infrastructure.Tests.Security.Passwords;

/// <summary>
/// Reading every format a deployment has ever written, while writing only one.
/// </summary>
/// <remarks>
/// This is what makes the algorithm a choice rather than a decision made once
/// and never revisited. A system arriving from elsewhere brings bcrypt entries;
/// a customer asks for Argon2id; a cost parameter is raised. In each case the
/// old entries stay readable and are rewritten as people sign in, and nobody
/// is asked to reset a password.
/// </remarks>
[Trait("Category", "Unit")]
public class PasswordHasherChainTests
{
    private readonly Pbkdf2PasswordHasher _pbkdf2 = new();
    private readonly FakeLegacyHasher _legacy = new();

    private PasswordHasherChain Chain() => new(_pbkdf2, [_legacy]);

    [Fact]
    public void It_writes_only_the_primary_format()
    {
        Chain().Hash("a password").Should().StartWith("$pbkdf2-sha256$");
    }

    [Fact]
    public void An_entry_in_the_old_format_still_verifies()
    {
        var stored = _legacy.Hash("a password");

        Chain().Verify("a password", stored)
            .Should().Be(PasswordVerification.SuccessRehashNeeded);
    }

    /// <summary>
    /// And the caller is told to rewrite it, or the old format never leaves.
    /// </summary>
    [Fact]
    public void An_entry_in_the_current_format_needs_no_rewrite()
    {
        var stored = _pbkdf2.Hash("a password");

        Chain().Verify("a password", stored).Should().Be(PasswordVerification.Success);
    }

    [Fact]
    public void A_wrong_password_against_an_old_entry_is_simply_wrong()
    {
        var stored = _legacy.Hash("a password");

        Chain().Verify("the wrong password", stored).Should().Be(PasswordVerification.Failed);
    }

    /// <summary>
    /// An entry nobody recognises still costs the same work, or the answer's
    /// duration says which formats this deployment knows.
    /// </summary>
    [Fact]
    public void An_unrecognised_entry_fails_without_shortcutting()
    {
        var chain = Chain();

        chain.Verify("a password", "$unknown-scheme$whatever")
            .Should().Be(PasswordVerification.Failed);
    }

    [Fact]
    public void A_missing_entry_behaves_the_same()
    {
        Chain().Verify("a password", null).Should().Be(PasswordVerification.Failed);
    }

    [Fact]
    public void The_chain_reads_whatever_any_member_reads()
    {
        var chain = Chain();

        chain.CanRead(_legacy.Hash("x")).Should().BeTrue();
        chain.CanRead(_pbkdf2.Hash("x")).Should().BeTrue();
        chain.CanRead("$unknown-scheme$whatever").Should().BeFalse();
    }

    /// <summary>
    /// Raising the cost is the same mechanism: the old entry is still this
    /// algorithm, and rewriting it is still the answer.
    /// </summary>
    [Fact]
    public void A_raised_cost_is_reported_the_same_way()
    {
        var cheap = new Pbkdf2PasswordHasher(new PasswordHashingOptions { Iterations = 100_000 });
        var chain = new PasswordHasherChain(_pbkdf2, [cheap]);

        chain.Verify("a password", cheap.Hash("a password"))
            .Should().Be(PasswordVerification.SuccessRehashNeeded);
    }

    /// <summary>Stands in for bcrypt or anything else already in a store.</summary>
    private sealed class FakeLegacyHasher : IPasswordHasher
    {
        private const string Prefix = "$legacy$";

        public string Hash(string password) => Prefix + password.GetHashCode(StringComparison.Ordinal);

        public bool CanRead(string encodedHash) =>
            encodedHash.StartsWith(Prefix, StringComparison.Ordinal);

        public PasswordVerification Verify(string password, string? encodedHash) =>
            encodedHash is not null && encodedHash == Hash(password)
                ? PasswordVerification.Success
                : PasswordVerification.Failed;
    }
}
