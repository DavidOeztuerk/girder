using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Http;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// The nested builders: settings that belong to one module, written where that
/// module is chosen.
/// </summary>
[Trait("Category", "Unit")]
public class GirderModulbaumeisterTests
{
    [Fact]
    public void UseRateLimiting_nimmt_das_Modul_mit_auf()
    {
        var zusammensetzung = Zusammensetzen(girder => girder.UseRateLimiting(rate => rate.PerOrigin()));

        zusammensetzung.Included.Should().Contain(GirderModule.RateLimiting);
    }

    [Fact]
    public void PerOrigin_stellt_den_Zaehlgegenstand_um()
    {
        var options = Optionen(girder => girder.UseDefaults().UseRateLimiting(rate => rate.PerOrigin()));

        options.Subject.Should().Be(RateLimitSubject.Origin);
    }

    [Fact]
    public void PerSubject_traegt_den_eigenen_Auszug_ein()
    {
        var options = Optionen(girder => girder
            .UseDefaults()
            .UseRateLimiting(rate => rate.PerSubject(context => context.Request.Headers["X-Tenant"].ToString())));

        options.Subject.Should().Be(RateLimitSubject.Custom);
        options.SubjectExtractor.Should().NotBeNull();
    }

    [Fact]
    public void Allowing_setzt_die_drei_Fenster()
    {
        var options = Optionen(girder => girder
            .UseDefaults()
            .UseRateLimiting(rate => rate.Allowing(perMinute: 5, perHour: 20, perDay: 100)));

        options.RequestsPerMinute.Should().Be(5);
        options.RequestsPerHour.Should().Be(20);
        options.RequestsPerDay.Should().Be(100);
    }

    /// <summary>
    /// The default written out loud changes nothing, which is the point: it can
    /// be there without altering behaviour, and its absence later is visible.
    /// </summary>
    [Fact]
    public void TrustNoForwardedHeaders_erklaert_die_Vorgabe_und_aendert_nichts()
    {
        var services = Dienste();
        services.AddGirder(Konfiguration(), Umgebung(), "test-service", girder => girder
            .UseRateLimiting(rate => rate.TrustNoForwardedHeaders().PerOrigin()));

        services.BuildServiceProvider().GetService<ForwardedHeaderTrust>().Should().BeNull();
    }

    [Fact]
    public void TrustForwardedHeadersFrom_erklaert_die_Vermittler()
    {
        var services = Dienste();
        services.AddGirder(Konfiguration(), Umgebung(), "test-service", girder => girder
            .UseRateLimiting(rate => rate.TrustForwardedHeadersFrom("10.0.0.7")));

        var provider = services.BuildServiceProvider();
        provider.GetService<ForwardedHeaderTrust>().Should().NotBeNull();
        provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>()
            .Value.KnownProxies.Should().ContainSingle();
    }

    /// <summary>
    /// Saying both is a contradiction, and the one that reads as safe is the one
    /// that would silently lose.
    /// </summary>
    [Fact]
    public void Beides_zu_sagen_wird_abgelehnt()
    {
        var widerspruch = () => Zusammensetzen(girder => girder
            .UseRateLimiting(rate => rate.TrustNoForwardedHeaders().TrustForwardedHeadersFrom("10.0.0.7")));

        widerspruch.Should().Throw<InvalidOperationException>().WithMessage("*contradicts*");
    }

    [Fact]
    public void Und_auch_in_der_anderen_Reihenfolge()
    {
        var widerspruch = () => Zusammensetzen(girder => girder
            .UseRateLimiting(rate => rate.TrustForwardedHeadersFrom("10.0.0.7").TrustNoForwardedHeaders()));

        widerspruch.Should().Throw<InvalidOperationException>().WithMessage("*contradicts*");
    }


    [Fact]
    public void UseJwt_ohne_Herkunft_der_Schluessel_wird_abgelehnt()
    {
        var ohne = () => Zusammensetzen(girder => girder.UseJwt(_ => { }));

        ohne.Should().Throw<InvalidOperationException>().WithMessage("*where the keys come from*");
    }

    [Fact]
    public void VerifyOnly_richtet_das_Schema_ein()
    {
        var services = Dienste();
        services.AddGirder(Konfiguration(), Umgebung(), "test-service", girder => girder
            .UseJwt(jwt => jwt.VerifyOnly(OeffentlicherSchluessel, "k1")));

        services.Should().Contain(d =>
            d.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService));
    }

    /// <summary>
    /// Two sources would leave it unclear which one decides, and the answer
    /// would be "whichever was written last", which nobody reads for.
    /// </summary>
    [Fact]
    public void Zwei_Herkuenfte_zu_nennen_wird_abgelehnt()
    {
        var zwei = () => Zusammensetzen(girder => girder.UseJwt(jwt => jwt
            .VerifyOnly(OeffentlicherSchluessel, "k1")
            .From("https://issuer.example")));

        zwei.Should().Throw<InvalidOperationException>().WithMessage("*already settled*");
    }

    /// <summary>A rotation needs both keys accepted at once.</summary>
    [Fact]
    public void AlsoVerify_nimmt_einen_zweiten_Schluessel_dazu()
    {
        var beide = () => Zusammensetzen(girder => girder.UseJwt(jwt => jwt
            .VerifyOnly(OeffentlicherSchluessel, "alt")
            .AlsoVerify(ZweiterSchluessel, "neu")));

        beide.Should().NotThrow();
    }

    private static readonly string OeffentlicherSchluessel = Schluesselpaar().oeffentlich;
    private static readonly string ZweiterSchluessel = Schluesselpaar().oeffentlich;

    private static (string oeffentlich, string privat) Schluesselpaar()
    {
        using var ecdsa = System.Security.Cryptography.ECDsa.Create(
            System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
                Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
    }

    private static DistributedRateLimitingOptions Optionen(Action<GirderBuilder> konfigurieren)
    {
        var services = Dienste();
        services.AddGirder(Konfiguration(), Umgebung(), "test-service", konfigurieren);
        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<DistributedRateLimitingOptions>>().Value;
    }

    private static GirderComposition Zusammensetzen(Action<GirderBuilder> konfigurieren)
    {
        var services = Dienste();
        services.AddGirder(Konfiguration(), Umgebung(), "test-service", konfigurieren);
        return services.BuildServiceProvider().GetRequiredService<GirderComposition>();
    }

    private static IServiceCollection Dienste() => new ServiceCollection();

    private static IConfiguration Konfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:Issuer"] = "test",
            ["JwtSettings:Audience"] = "test"
        }).Build();

    private static IHostEnvironment Umgebung() => new Umgebungsstub();

    private sealed class Umgebungsstub : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "test-service";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
