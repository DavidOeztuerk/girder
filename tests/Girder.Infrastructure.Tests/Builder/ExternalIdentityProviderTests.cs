using System.Security.Cryptography;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Keys;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// A service must be able to verify tokens an external provider issued.
/// </summary>
/// <remarks>
/// The point is not convenience. A library whose own sign-in cannot be swapped
/// for Keycloak, Zitadel or authentik is itself the lock-in it claims to
/// prevent — so the same verification path has to work from a locally
/// configured key and from a provider's published key set.
/// </remarks>
[Trait("Category", "Unit")]
public class ExternalIdentityProviderTests
{
    private const string Authority = "https://keycloak.example/realms/demo";

    [Fact]
    public void An_authority_configures_the_bearer_scheme_to_discover_its_keys()
    {
        var app = Build(o => o.Authority = Authority);

        var bearer = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.Authority.Should().Be(Authority);
    }

    /// <summary>
    /// With a provider there is no local key to configure, and demanding one
    /// would make the swap impossible.
    /// </summary>
    [Fact]
    public void An_authority_needs_no_local_keys()
    {
        var compose = () => Build(o => o.Authority = Authority);

        compose.Should().NotThrow();
    }

    /// <summary>
    /// And such a service issues nothing — the provider does.
    /// </summary>
    [Fact]
    public void A_service_behind_a_provider_holds_no_issuing_service()
    {
        var app = Build(o => o.Authority = Authority);

        using var scope = app.Services.CreateScope();
        var resolve = () => scope.ServiceProvider.GetRequiredService<IJwtService>();

        resolve.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// A provider plus a local signing key is a service that issues its own
    /// tokens and also accepts the provider's — the shape a migration has.
    /// </summary>
    [Fact]
    public void A_provider_and_a_local_key_can_coexist()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = SigningKey.FromEcdsaPrivateKey(
            Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()), "local");

        var app = Build(o =>
        {
            o.Authority = Authority;
            o.SigningKey = privateKey;
        });

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetService<IJwtService>().Should().NotBeNull();
        app.Services.GetRequiredService<KeyRing>().CanIssue.Should().BeTrue();
    }

    /// <summary>
    /// Without an authority nothing changes: the shared secret path stands.
    /// </summary>
    [Fact]
    public void Without_an_authority_the_local_configuration_is_unchanged()
    {
        var app = Build(configure: null);

        var bearer = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.Authority.Should().BeNull();
        app.Services.GetRequiredService<KeyRing>().CanIssue.Should().BeTrue();
    }

    private static WebApplication Build(Action<JwtOptions>? configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "a-test-secret-long-enough-to-sign-with-hmac-sha256",
            ["JwtSettings:Issuer"] = "girder-tests",
            ["JwtSettings:Audience"] = "girder-tests"
        });

        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddJwtAuthentication(configure));

        return builder.Build();
    }
}
