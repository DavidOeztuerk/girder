using System.Security.Cryptography;
using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.Authorization;
using Noelia.Infrastructure.Builder;
using Noelia.Infrastructure.Extensions;
using Noelia.Infrastructure.Security;
using Noelia.Infrastructure.Security.Keys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Noelia.Infrastructure.Tests.Builder;

/// <summary>
/// That every module registers whole modules.
/// </summary>
/// <remarks>
/// A catalogue entry that registers a consumer without its dependency, or that
/// carries a name from one thing and the behaviour of another, produces a
/// container that builds and a service that fails later — on the first request
/// that touches the missing half, which is to say in production.
/// </remarks>
[Trait("Category", "Unit")]
public class DerKatalogHaeltWasErRegistriertTests
{
    // ── Der Schlüsselbund und sein Verbraucher gehören zusammen ──

    [Fact]
    public void VerifyOnly_legt_den_Schluesselbund_ab()
    {
        var (oeffentlich, _) = Schluesselpaar();

        var anbieter = Gebaut(noelia => noelia.UseJwt(jwt => jwt.VerifyOnly(oeffentlich, "k1")));

        anbieter.GetService<KeyRing>().Should().NotBeNull();
    }

    [Fact]
    public void Issue_legt_den_Schluesselbund_ab()
    {
        var (_, privat) = Schluesselpaar();

        var anbieter = Gebaut(noelia => noelia.UseJwt(jwt => jwt.Issue(privat, "k1")));

        anbieter.GetRequiredService<KeyRing>().SigningKey.Should().NotBeNull();
    }

    [Fact]
    public void FromSharedSecret_legt_den_Schluesselbund_ab()
    {
        var anbieter = Gebaut(noelia => noelia.UseJwt(jwt => jwt.FromSharedSecret()));

        anbieter.GetService<KeyRing>().Should().NotBeNull();
    }

    [Theory]
    [InlineData("VerifyOnly")]
    [InlineData("Issue")]
    [InlineData("FromSharedSecret")]
    public void Mit_Schluesseln_laesst_sich_IJwtService_aufloesen(string weg)
    {
        var (oeffentlich, privat) = Schluesselpaar();

        var anbieter = Gebaut(noelia => noelia.UseDefaults().UseJwt(jwt =>
        {
            _ = weg switch
            {
                "VerifyOnly" => jwt.VerifyOnly(oeffentlich, "k1"),
                "Issue" => jwt.Issue(privat, "k1"),
                _ => jwt.FromSharedSecret()
            };
        }));

        using var bereich = anbieter.CreateScope();
        bereich.ServiceProvider.GetService<IJwtService>().Should().NotBeNull();
    }

    /// <summary>
    /// The coupling, from the other side: without keys there is no token service
    /// either. Half of the pair is worse than neither, because it builds.
    /// </summary>
    [Fact]
    public void Ohne_UseJwt_gibt_es_weder_Schluesselbund_noch_IJwtService()
    {
        var anbieter = Gebaut(noelia => noelia.UseDefaults());

        anbieter.GetService<KeyRing>().Should().BeNull();
        using var bereich = anbieter.CreateScope();
        bereich.ServiceProvider.GetService<IJwtService>().Should().BeNull();
    }

    /// <summary>
    /// What the module keeps regardless: neither needs a key.
    /// </summary>
    [Fact]
    public void Das_Jwt_Modul_haelt_weiterhin_Totp_und_Fehlermeldungen()
    {
        var anbieter = Gebaut(noelia => noelia.UseDefaults());

        anbieter.GetService<ITotpService>().Should().NotBeNull();
        anbieter.GetService<Noelia.Core.Exceptions.IErrorMessageService>().Should().NotBeNull();
    }

    // ── Ein Modul heißt, was es tut ──

    /// <summary>
    /// <c>[RequirePermission]</c> names a <c>Permission:</c> policy that only
    /// <c>PermissionPolicyProvider</c> answers. Without it the framework rejects
    /// the request as "policy not found" — on every call to exactly the endpoints
    /// the attribute was meant to protect.
    /// </summary>
    [Fact]
    public void Authorization_registriert_den_Berechtigungsanbieter()
    {
        var anbieter = Gebaut(noelia => noelia.UseDefaults());

        anbieter.GetRequiredService<IAuthorizationPolicyProvider>()
            .Should().BeOfType<PermissionPolicyProvider>();
    }

    /// <summary>
    /// The other half is a module of its own, because it needs a store to ask
    /// about resources — and its two handlers take that store as a constructor
    /// argument, so a default set carrying it could not be built at all.
    /// </summary>
    [Fact]
    public void Ressourcenpolitik_ist_ein_eigenes_Modul_und_nicht_in_der_Vorgabe()
    {
        var vorgabe = Dienste(noelia => noelia.UseDefaults());
        var mit = Dienste(noelia => noelia.UseDefaults().Use(NoeliaModule.ResourceAuthorization));

        vorgabe.Any(RessourcenHandler).Should().BeFalse();
        mit.Any(RessourcenHandler).Should().BeTrue();
    }

    /// <summary>And it names what it needs, rather than failing on the first
    /// authorized request.</summary>
    [Fact]
    public void Ressourcenpolitik_meldet_ihren_fehlenden_Anbieter()
    {
        var services = Dienste(noelia => noelia.UseDefaults().Use(NoeliaModule.ResourceAuthorization));
        var anforderungen = services.BuildServiceProvider()
            .GetRequiredService<Noelia.Abstractions.Hosting.ProviderRequirements>();

        anforderungen.Unmet(services.BuildServiceProvider())
            .Should().Contain(r => r.ServiceType.Name == "IResourceAuthorizationService");
    }

    /// <summary>
    /// The two that take <c>IResourceAuthorizationService</c> — named, not
    /// matched on a substring: the permission half registers a
    /// <c>ResourceOwnerHandler</c> of its own, which needs no such service.
    /// </summary>
    private static readonly Func<ServiceDescriptor, bool> RessourcenHandler = d =>
        d.ServiceType == typeof(IAuthorizationHandler)
        && d.ImplementationType is not null
        && d.ImplementationType.Name is "ResourceAuthorizationHandler" or "OwnershipAuthorizationHandler";

    private static ServiceProvider Gebaut(Action<NoeliaBuilder> konfigurieren) =>
        Dienste(konfigurieren).BuildServiceProvider();

    private static IServiceCollection Dienste(Action<NoeliaBuilder> konfigurieren)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNoelia(Konfiguration(), Umgebung(), "test-service", konfigurieren);
        return services;
    }

    private static IConfiguration Konfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "test",
            ["JwtSettings:Audience"] = "test",
            ["JwtSettings:Secret"] = "ein-hinreichend-langes-testgeheimnis-fuer-hmac-256"
        }).Build();

    private static IHostEnvironment Umgebung() => new Umgebungsstub();

    private static (string oeffentlich, string privat) Schluesselpaar()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
                Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
    }

    private sealed class Umgebungsstub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test-service";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
