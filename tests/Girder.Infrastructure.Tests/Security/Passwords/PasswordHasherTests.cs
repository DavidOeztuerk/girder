using Girder.Abstractions.Security.Passwords;
using Girder.Infrastructure.Security.Passwords;

namespace Girder.Infrastructure.Tests.Security.Passwords;

/// <summary>
/// What the store holds must verify the right password, refuse every other,
/// and reveal nothing — including through how long the answer takes.
/// </summary>
[Trait("Category", "Unit")]
public class PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void The_right_password_verifies()
    {
        var stored = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("correct horse battery staple", stored)
            .Should().Be(PasswordVerification.Success);
    }

    [Fact]
    public void A_wrong_password_does_not()
    {
        var stored = _hasher.Hash("correct horse battery staple");

        _hasher.Verify("correct horse battery stapler", stored)
            .Should().Be(PasswordVerification.Failed);
    }

    /// <summary>
    /// Two people with the same password must not share an entry, or one leaked
    /// table tells an attacker which accounts to try together.
    /// </summary>
    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        _hasher.Hash("the same password").Should().NotBe(_hasher.Hash("the same password"));
    }

    /// <summary>
    /// The stored form is PHC-shaped: <c>$id$params$salt$hash</c>. Self-describing,
    /// and the shape other tools already speak — a system that leaves Girder
    /// takes its hashes with it, and a system arriving can be read.
    /// </summary>
    [Fact]
    public void The_stored_form_names_its_algorithm_and_cost()
    {
        var stored = _hasher.Hash("anything");

        stored.Should().StartWith("$pbkdf2-sha256$i=");
        stored.Split('$').Should().HaveCount(5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("$pbkdf2-sha256$i=600000$not-base64$also-not")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA")]
    [InlineData("$2b$12$abcdefghijklmnopqrstuv")]
    public void An_entry_this_hasher_cannot_read_fails_rather_than_throwing(string stored)
    {
        // A store can hold anything after a migration from another system, and
        // a crash there turns one unreadable row into a broken sign-in endpoint.
        // Failing lets a composite hasher try the next reader.
        _hasher.Verify("any password", stored).Should().Be(PasswordVerification.Failed);
    }

    /// <summary>
    /// Raising the cost has to be noticed, or it never reaches the entries that
    /// were written before it.
    /// </summary>
    [Fact]
    public void An_entry_below_the_current_cost_asks_to_be_rewritten()
    {
        var cheap = new Pbkdf2PasswordHasher(new PasswordHashingOptions { Iterations = 100_000 });
        var current = new Pbkdf2PasswordHasher(new PasswordHashingOptions { Iterations = 600_000 });

        var stored = cheap.Hash("a password");

        current.Verify("a password", stored).Should().Be(PasswordVerification.SuccessRehashNeeded);
    }

    [Fact]
    public void An_entry_at_the_current_cost_does_not()
    {
        var stored = _hasher.Hash("a password");

        _hasher.Verify("a password", stored).Should().Be(PasswordVerification.Success);
    }

    /// <summary>
    /// A wrong password against an outdated entry is still a wrong password.
    /// </summary>
    [Fact]
    public void A_wrong_password_against_an_outdated_entry_is_simply_wrong()
    {
        var cheap = new Pbkdf2PasswordHasher(new PasswordHashingOptions { Iterations = 100_000 });
        var stored = cheap.Hash("a password");

        _hasher.Verify("the wrong password", stored).Should().Be(PasswordVerification.Failed);
    }

    /// <summary>
    /// No account is not a separate code path.
    /// </summary>
    /// <remarks>
    /// Passing null is what a caller has when the address is unknown. The
    /// alternative — a separate <c>DummyVerify()</c> the caller must remember —
    /// is a call that eventually is not made, and then the time an answer takes
    /// says whether the account exists.
    /// </remarks>
    [Fact]
    public void A_missing_entry_verifies_nothing_and_answers_like_a_wrong_password()
    {
        _hasher.Verify("any password", null).Should().Be(PasswordVerification.Failed);
    }
}
