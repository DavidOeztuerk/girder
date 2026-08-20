using System.Security.Cryptography;
using Girder.Core.Identity;
using Girder.InMemory.Security;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Identity;
using Girder.Infrastructure.Security.Keys;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// Where a service gets its keys, and what that lets it do.
/// </summary>
/// <remarks>
/// Until now a service could only hold a shared secret, so every service able
/// to verify a token was also able to mint one — for any subject, with any
/// role. These tests pin that a consumer can now hold verification material
/// alone, and that the old configuration keeps working unchanged.
/// </remarks>
[Trait("Category", "Unit")]
public class JwtKeySourceTests
{
    private const string Secret = "a-test-secret-long-enough-to-sign-with-hmac-sha256";

    /// <summary>
    /// The path every existing consumer is on. It must not change in a minor
    /// version — a library that breaks sign-in on a patch is one nobody updates.
    /// </summary>
    [Fact]
    public void Without_configured_keys_the_shared_secret_is_used()
    {
        var app = Build(configure: null);

        var ring = app.Services.GetRequiredService<KeyRing>();

        ring.CanIssue.Should().BeTrue();
        ring.SigningKey!.Algorithm.Should().Be("HS256");
        ring.SigningKey.SeparatesIssuingFromVerifying.Should().BeFalse();
    }

    [Fact]
    public void A_service_given_only_a_public_key_cannot_issue()
    {
        var pair = NewPair();
        var app = Build(o => o.ValidationKeys.Add(pair.Public));

        app.Services.GetRequiredService<KeyRing>().CanIssue.Should().BeFalse();
    }

    /// <summary>
    /// And it must fail when asked, not quietly produce something unusable.
    /// </summary>
    [Fact]
    public async Task Asking_a_verifying_service_to_issue_fails_and_says_why()
    {
        var pair = NewPair();
        var app = Build(o => o.ValidationKeys.Add(pair.Public));

        using var scope = app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var issue = async () => await jwt.GenerateTokenAsync(Claims(SubjectId.New()));

        (await issue.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*no signing key*");
    }

    /// <summary>
    /// The whole point: one service issues, another verifies, and the verifier
    /// never held anything it could have signed with.
    /// </summary>
    [Fact]
    public async Task A_token_from_the_issuer_verifies_at_a_consumer_holding_only_the_public_key()
    {
        var pair = NewPair();
        var subject = SubjectId.New();

        var issuer = Build(o =>
        {
            o.SigningKey = pair.Private;
            o.ValidationKeys.Add(pair.Public);
        });
        var consumer = Build(o => o.ValidationKeys.Add(pair.Public));

        using var issuerScope = issuer.Services.CreateScope();
        var issued = await issuerScope.ServiceProvider.GetRequiredService<IJwtService>()
            .GenerateTokenAsync(Claims(subject));

        using var consumerScope = consumer.Services.CreateScope();
        var claims = await consumerScope.ServiceProvider.GetRequiredService<IJwtService>()
            .ValidateTokenAsync(issued.AccessToken);

        claims.Should().NotBeNull();
        consumerScope.ServiceProvider.GetRequiredService<IPrincipalFactory>()
            .Create(claims!).Principal!.Subject.Should().Be(subject);
    }

    /// <summary>
    /// A consumer holding the wrong pair must refuse, or the previous test
    /// would pass for the wrong reason.
    /// </summary>
    [Fact]
    public async Task A_consumer_holding_a_different_public_key_refuses_the_token()
    {
        var issuerPair = NewPair();
        var strangerPair = NewPair();

        var issuer = Build(o =>
        {
            o.SigningKey = issuerPair.Private;
            o.ValidationKeys.Add(issuerPair.Public);
        });
        var consumer = Build(o => o.ValidationKeys.Add(strangerPair.Public));

        using var issuerScope = issuer.Services.CreateScope();
        var issued = await issuerScope.ServiceProvider.GetRequiredService<IJwtService>()
            .GenerateTokenAsync(Claims(SubjectId.New()));

        using var consumerScope = consumer.Services.CreateScope();
        var claims = await consumerScope.ServiceProvider.GetRequiredService<IJwtService>()
            .ValidateTokenAsync(issued.AccessToken);

        claims.Should().BeNull();
    }

    /// <summary>
    /// A service configured with a key pair has no shared secret at all, and
    /// must not be asked for one.
    /// </summary>
    /// <remarks>
    /// The earlier tests all set <c>JwtSettings:Secret</c> alongside the pair,
    /// so none of them would have noticed that the service still demanded it.
    /// </remarks>
    [Fact]
    public async Task An_asymmetric_service_needs_no_shared_secret()
    {
        var pair = NewPair();
        var subject = SubjectId.New();

        var app = BuildWithoutSecret(o =>
        {
            o.SigningKey = pair.Private;
            o.ValidationKeys.Add(pair.Public);
        });

        using var scope = app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var issued = await jwt.GenerateTokenAsync(Claims(subject));
        var claims = await jwt.ValidateTokenAsync(issued.AccessToken);

        claims.Should().NotBeNull();
    }

    /// <summary>
    /// And a service that revokes nothing must still be able to issue and
    /// verify. Revocation is the upgrade, not the entry price.
    /// </summary>
    [Fact]
    public async Task A_service_without_a_revocation_store_still_issues_and_verifies()
    {
        var pair = NewPair();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "girder-tests",
            ["JwtSettings:Audience"] = "girder-tests"
        });
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddJwtAuthentication(o =>
            {
                o.SigningKey = pair.Private;
                o.ValidationKeys.Add(pair.Public);
            }).AddPrincipal());
        // Deliberately no AddInMemoryTokenRevocation().
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var issued = await jwt.GenerateTokenAsync(Claims(SubjectId.New()));

        (await jwt.ValidateTokenAsync(issued.AccessToken)).Should().NotBeNull();
    }

    /// <summary>
    /// Asking such a service to revoke must fail loudly. A writer that quietly
    /// did nothing would report "signed out everywhere" to someone whose tokens
    /// keep working.
    /// </summary>
    [Fact]
    public async Task Revoking_without_a_store_fails_and_names_the_fix()
    {
        var pair = NewPair();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "girder-tests",
            ["JwtSettings:Audience"] = "girder-tests"
        });
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddJwtAuthentication(o =>
            {
                o.SigningKey = pair.Private;
                o.ValidationKeys.Add(pair.Public);
            }));
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();

        var revoke = async () => await jwt.RevokeTokenAsync("some-jti", "some-subject");

        (await revoke.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*AddInMemoryTokenRevocation*");
    }

    private static WebApplication BuildWithoutSecret(Action<JwtOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "girder-tests",
            ["JwtSettings:Audience"] = "girder-tests"
        });

        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddJwtAuthentication(configure).AddPrincipal());
        builder.Services.AddInMemoryTokenRevocation();

        return builder.Build();
    }

    private static (SigningKey Private, SigningKey Public) NewPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kid = Guid.NewGuid().ToString("N")[..8];

        return (
            SigningKey.FromEcdsaPrivateKey(Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), kid),
            SigningKey.FromEcdsaPublicKey(Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()), kid));
    }

    private static UserClaims Claims(SubjectId subject) => new()
    {
        UserId = subject.ToString(),
        Email = "someone@example.test"
    };

    private static WebApplication Build(Action<JwtOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = Secret,
            ["JwtSettings:Issuer"] = "girder-tests",
            ["JwtSettings:Audience"] = "girder-tests"
        });

        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddJwtAuthentication(configure).AddPrincipal());
        builder.Services.AddInMemoryTokenRevocation();

        return builder.Build();
    }
}
