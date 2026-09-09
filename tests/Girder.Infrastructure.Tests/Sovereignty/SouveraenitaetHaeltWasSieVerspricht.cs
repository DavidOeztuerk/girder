using Girder.Abstractions.Hosting;
using Girder.Core.Logging;
using Girder.Infrastructure.Audit;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Logging;
using Girder.Infrastructure.Sovereignty;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Events;
using Serilog.Parsing;

namespace Girder.Infrastructure.Tests.Sovereignty;

/// <summary>
/// What 4.3.0 promised, measured rather than assumed.
/// </summary>
[Trait("Category", "Unit")]
public class SouveraenitaetHaeltWasSieVerspricht
{
    // ── Die Maskierung trifft Werte, nicht Namen von Dingen ──

    /// <summary>
    /// A secret's <em>name</em> is not a secret — it is how you tell which one
    /// failed to load. Thirteen log statements in Girder itself say
    /// <c>{SecretName}</c>.
    /// </summary>
    [Theory]
    [InlineData("SecretName")]
    [InlineData("TokenId")]
    [InlineData("TokenEndpoint")]
    [InlineData("AuthorizationPolicy")]
    [InlineData("RefreshTokenLifetime")]
    [InlineData("TokenSessionService")]
    public void Ein_Name_einer_Sache_ist_nicht_ihr_Wert(string schluessel)
    {
        Maskiert(schluessel, "sichtbarer-wert").Should().Be("sichtbarer-wert");
    }

    [Theory]
    [InlineData("Password")]
    [InlineData("Token")]
    [InlineData("Secret")]
    [InlineData("Authorization")]
    [InlineData("IBAN")]
    public void Die_fuenf_geforderten_Schluessel_werden_maskiert(string schluessel)
    {
        Maskiert(schluessel, "geheim").Should().Be(SensitiveValuePatterns.Masked);
    }

    /// <summary>
    /// The proof that it is the shared list and not a second one: these names
    /// appear only in <see cref="SensitiveFieldNames"/>.
    /// </summary>
    [Theory]
    [InlineData("Ssn")]
    [InlineData("Bic")]
    [InlineData("CreditCard")]
    [InlineData("PassportNumber")]
    public void Es_ist_die_gemeinsame_Liste(string schluessel)
    {
        Maskiert(schluessel, "geheim").Should().Be(SensitiveValuePatterns.Masked);
    }

    /// <summary>
    /// One mask, because a reader who greps for redactions must find them all.
    /// </summary>
    [Fact]
    public void Es_gibt_nur_eine_Maske()
    {
        DataMaskingEnricher.Mask.Should().Be(SensitiveValuePatterns.Masked);
    }

    // ── Der Revisions-Hash lässt sich nicht durch Verschieben fälschen ──

    /// <summary>
    /// The fields were joined with a separator that may occur inside them, so
    /// content shifted across a field boundary produced the same hash — and a
    /// tamper-evidence mechanism that accepts a forgery is not one.
    /// </summary>
    /// <remarks>
    /// <c>CorrelationId</c> comes from a header the caller sets, and the state
    /// snapshots are JSON, so both sides of the boundary are reachable.
    /// </remarks>
    [Fact]
    public void Verschobener_Inhalt_ergibt_einen_anderen_Hash()
    {
        var echt = Ereignis(korrelation: "a|b", vorher: "c");
        var gefaelscht = Ereignis(korrelation: "a", vorher: "b|c");

        gefaelscht.Hash.Should().NotBe(echt.Hash);
    }

    [Fact]
    public void Ein_unveraendertes_Ereignis_besteht_seine_Pruefung()
    {
        Ereignis(korrelation: "a|b", vorher: "c").VerifyHash().Should().BeTrue();
    }

    // ── Die Speicherreihenfolge ist die Kettenreihenfolge ──

    /// <summary>
    /// The chain was advanced under a lock but written to the sink outside it,
    /// so under load the stored order could differ from the hashed order — and a
    /// verifier walking the store then sees a broken chain on an honest system.
    /// </summary>
    [Fact]
    public async Task Unter_Nebenlaeufigkeit_ist_die_Speicherreihenfolge_die_Kettenreihenfolge()
    {
        // A sink that yields, because a sovereign one is a database, a file or a
        // network hop. One that answers synchronously hides the very
        // interleaving this asks about.
        var senke = new LangsameSenke();
        var dienst = new AuditTrailService(senke, NullLogger<AuditTrailService>.Instance);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => Task.Run(() =>
            dienst.RecordAsync("usr", "as self", "Update", $"Doc/{i}", after: $"stand-{i}"))));

        var gespeichert = senke.Geschrieben;
        gespeichert.Should().HaveCount(200);

        string? vorheriger = null;
        foreach (var ereignis in gespeichert)
        {
            ereignis.PreviousHash.Should().Be(vorheriger, "the store must read back as the chain was built");
            vorheriger = ereignis.Hash;
        }
    }

    // ── Die strenge Egress-Vorgabe lässt sich wirklich straffen ──

    [Fact]
    public void Loopback_laesst_sich_abschalten()
    {
        var richtlinie = Egress(s => s.WithoutLoopback().Allow("openbao.internal"));

        richtlinie.IsAllowed(new Uri("http://127.0.0.1:8200")).Should().BeFalse();
        richtlinie.IsAllowed(new Uri("https://openbao.internal")).Should().BeTrue();
    }

    [Fact]
    public void Private_Netze_lassen_sich_abschalten()
    {
        var richtlinie = Egress(s => s.WithoutPrivateNetworks().Allow("openbao.internal"));

        richtlinie.IsAllowed(new Uri("http://10.1.2.3")).Should().BeFalse();
    }

    [Fact]
    public void Ohne_Abschalten_bleiben_beide_erlaubt()
    {
        var richtlinie = Egress(s => s.Allow("openbao.internal"));

        richtlinie.IsAllowed(new Uri("http://127.0.0.1:8200")).Should().BeTrue();
        richtlinie.IsAllowed(new Uri("http://10.1.2.3")).Should().BeTrue();
    }

    // ── Die souveräne Plattform ist ein Modul wie jedes andere ──

    /// <summary>
    /// Everything Girder sets up appears in the composition, so a service can
    /// report what it runs. A bundle that registers past it is invisible there
    /// and cannot be dropped with a reason.
    /// </summary>
    [Fact]
    public void Die_souveraene_Plattform_steht_in_der_Zusammensetzung()
    {
        var zusammensetzung = Zusammensetzen(girder => girder
            .UseDefaults()
            .AddSovereignPlatform(s => s.Allow("openbao.internal")));

        zusammensetzung.Included.Should().Contain(GirderModule.SovereignPlatform);
    }

    [Fact]
    public void Sie_laesst_sich_mit_Grund_abwaehlen()
    {
        var zusammensetzung = Zusammensetzen(girder => girder
            .UseDefaults()
            .AddSovereignPlatform(s => s.Allow("openbao.internal"))
            .Without(GirderModule.SovereignPlatform, "Testumgebung ruft absichtlich nach draußen"));

        zusammensetzung.Included.Should().NotContain(GirderModule.SovereignPlatform);
        zusammensetzung.Excluded[GirderModule.SovereignPlatform].Should().Contain("Testumgebung");
    }

    /// <summary>
    /// And nothing of it is set up when it was dropped — an egress policy
    /// registered anyway would still refuse the calls the reason allowed for.
    /// </summary>
    [Fact]
    public void Abgewaehlt_wird_auch_nichts_eingerichtet()
    {
        var dienste = Dienste(girder => girder
            .UseDefaults()
            .AddSovereignPlatform(s => s.Allow("openbao.internal"))
            .Without(GirderModule.SovereignPlatform, "Testumgebung"));

        dienste.BuildServiceProvider().GetService<IAuditTrailService>().Should().BeNull();
    }

    /// <summary>Writes in the order it was called, and takes a turn to do it.</summary>
    private sealed class LangsameSenke : ISovereignAuditSink
    {
        private readonly List<AuditEvent<string>> _geschrieben = [];

        public IReadOnlyList<AuditEvent<string>> Geschrieben
        {
            get { lock (_geschrieben) { return [.. _geschrieben]; } }
        }

        public async Task WriteAsync<T>(AuditEvent<T> auditEvent, CancellationToken cancellationToken = default)
        {
            await Task.Yield();

            lock (_geschrieben)
            {
                _geschrieben.Add((AuditEvent<string>)(object)auditEvent);
            }
        }
    }

    // ── Hilfen ──

    private static object? Maskiert(string schluessel, string wert)
    {
        var ereignis = new LogEvent(
            DateTimeOffset.UtcNow, LogEventLevel.Information, null,
            new MessageTemplate("t", []),
            [new LogEventProperty(schluessel, new ScalarValue(wert))]);

        new DataMaskingEnricher().Enrich(ereignis, null!);

        return ((ScalarValue)ereignis.Properties[schluessel]).Value;
    }

    private static AuditEvent<string> Ereignis(string korrelation, string vorher) =>
        new AuditEvent<string>
        {
            Id = "feste-id",
            Timestamp = DateTimeOffset.UnixEpoch,
            ActorId = "usr",
            Capacity = "as self",
            Action = "Update",
            Resource = "Doc/1",
            CorrelationId = korrelation,
            BeforeStateJson = vorher,
            AfterStateJson = "d"
        }.WithComputedHash();

    private static IEgressPolicy Egress(Action<SovereignPlatformBuilder> konfigurieren)
    {
        var dienste = Dienste(girder => girder.AddSovereignPlatform(konfigurieren));
        return dienste.BuildServiceProvider().GetRequiredService<IEgressPolicy>();
    }

    private static GirderComposition Zusammensetzen(Action<GirderBuilder> konfigurieren) =>
        Dienste(konfigurieren).BuildServiceProvider().GetRequiredService<GirderComposition>();

    private static IServiceCollection Dienste(Action<GirderBuilder> konfigurieren)
    {
        var dienste = new ServiceCollection();
        dienste.AddLogging();
        dienste.AddSingleton(Konfiguration());
        dienste.AddGirder(Konfiguration(), Umgebung(), "test-service", konfigurieren);
        return dienste;
    }

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
