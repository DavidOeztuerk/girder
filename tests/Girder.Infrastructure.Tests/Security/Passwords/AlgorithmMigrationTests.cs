using Girder.Abstractions.Security.Passwords;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Passwords;
using Girder.Passwords.Argon2;
using Girder.Passwords.BCrypt;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Security.Passwords;

/// <summary>
/// Changing the algorithm, and arriving with someone else's entries.
/// </summary>
/// <remarks>
/// The reason the port exists. A deployment that cannot change its password
/// algorithm without asking everyone to reset has not chosen an algorithm — it
/// has been given one.
/// </remarks>
[Trait("Category", "Unit")]
public class AlgorithmMigrationTests
{
    private const string Password = "a-long-enough-password";

    /// <summary>
    /// Nothing registered: PBKDF2, because it needs no package and no licence.
    /// </summary>
    [Fact]
    public void Without_a_choice_the_dependency_free_one_writes()
    {
        var hasher = Build();

        hasher.Hash(Password).Should().StartWith("$pbkdf2-sha256$");
    }

    [Fact]
    public void Registering_argon2_makes_it_the_one_that_writes()
    {
        var hasher = Build(services => services.AddArgon2Passwords());

        hasher.Hash(Password).Should().StartWith("$argon2id$");
    }

    [Fact]
    public void Registering_bcrypt_makes_it_the_one_that_writes()
    {
        var hasher = Build(services => services.AddBCryptPasswords(workFactor: 4));

        hasher.Hash(Password).Should().StartWith("$2");
    }

    /// <summary>
    /// A system arriving with bcrypt entries: they verify, and each sign-in
    /// moves one person to the format this deployment writes.
    /// </summary>
    [Fact]
    public void Entries_from_the_old_system_verify_and_ask_to_be_rewritten()
    {
        var theirs = new BCryptPasswordHasher(workFactor: 4).Hash(Password);
        var hasher = Build(services => services.AddBCryptPasswordReader(workFactor: 4));

        hasher.Verify(Password, theirs).Should().Be(PasswordVerification.SuccessRehashNeeded);
        hasher.Hash(Password).Should().StartWith("$pbkdf2-sha256$", "new entries use the current format");
    }

    /// <summary>
    /// And a wrong password against one of those entries is simply wrong — the
    /// migration must not turn into a way in.
    /// </summary>
    [Fact]
    public void A_wrong_password_against_an_old_entry_is_refused()
    {
        var theirs = new BCryptPasswordHasher(workFactor: 4).Hash(Password);
        var hasher = Build(services => services.AddBCryptPasswordReader(workFactor: 4));

        hasher.Verify("something else entirely", theirs).Should().Be(PasswordVerification.Failed);
    }

    /// <summary>
    /// Moving the other way is the same mechanism, which is the point: none of
    /// this is special-cased for one direction.
    /// </summary>
    [Fact]
    public void Moving_to_argon2_reads_what_pbkdf2_wrote()
    {
        var old = new Pbkdf2PasswordHasher().Hash(Password);

        var hasher = Build(services => services
            .AddArgon2Passwords()
            .AddPbkdf2PasswordReader());

        hasher.Verify(Password, old).Should().Be(PasswordVerification.SuccessRehashNeeded);
        hasher.Hash(Password).Should().StartWith("$argon2id$");
    }

    /// <summary>Three formats at once, which a long-lived system will have.</summary>
    [Fact]
    public void Several_old_formats_can_be_read_at_the_same_time()
    {
        var bcrypt = new BCryptPasswordHasher(workFactor: 4).Hash(Password);
        var pbkdf2 = new Pbkdf2PasswordHasher().Hash(Password);

        var hasher = Build(services => services
            .AddArgon2Passwords()
            .AddBCryptPasswordReader(workFactor: 4)
            .AddPbkdf2PasswordReader());

        hasher.Verify(Password, bcrypt).Should().Be(PasswordVerification.SuccessRehashNeeded);
        hasher.Verify(Password, pbkdf2).Should().Be(PasswordVerification.SuccessRehashNeeded);
        hasher.Verify(Password, hasher.Hash(Password)).Should().Be(PasswordVerification.Success);
    }

    private static IPasswordHasher Build(Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddPasswordHashing());
        configure?.Invoke(builder.Services);

        return builder.Build().Services.GetRequiredService<IPasswordHasher>();
    }
}
