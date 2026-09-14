using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.Builder;
using Noelia.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Noelia.Infrastructure.Tests.Builder;

/// <summary>
/// The middle that was missing: a default you can see, and depart from with a
/// reason.
/// </summary>
/// <remarks>
/// One entry point decided thirteen modules for you; the other replaced the
/// default rather than adjusting it, so taking it cost thirteen modules and
/// everything that stood outside the module system, silently. What is pinned
/// here is that neither is possible any more.
/// </remarks>
[Trait("Category", "Unit")]
public class NoeliaBuilderTests
{
    private static readonly NoeliaModule Erfunden = new("Test.Erfunden");
    private static readonly NoeliaModule Zweitens = new("Test.Zweitens");

    /// <summary>
    /// Like Entity Framework without a provider: nothing configured means
    /// nothing runs, and it says so rather than guessing.
    /// </summary>
    [Fact]
    public void Ohne_UseDefaults_wird_nichts_registriert()
    {
        var zusammensetzung = Zusammensetzen(_ => { });

        zusammensetzung.Included.Should().BeEmpty();
    }

    [Fact]
    public void UseDefaults_nimmt_die_Vorgabe_auf()
    {
        var zusammensetzung = Zusammensetzen(noelia => noelia.UseDefaults());

        zusammensetzung.Included.Should().Contain(NoeliaModule.Caching);
        zusammensetzung.Included.Should().Contain(NoeliaModule.RateLimiting);
        zusammensetzung.Included.Should().HaveCountGreaterThan(10);
    }

    [Fact]
    public void Without_nimmt_ein_Modul_aus_der_Vorgabe_heraus()
    {
        var zusammensetzung = Zusammensetzen(noelia => noelia
            .UseDefaults()
            .Without(NoeliaModule.Caching, "ADR-0013: Einwilligung darf nicht zwischengespeichert werden"));

        zusammensetzung.Included.Should().NotContain(NoeliaModule.Caching);
        zusammensetzung.Excluded[NoeliaModule.Caching].Should().Contain("ADR-0013");
    }

    /// <summary>
    /// The reason is the point. An empty one is the same omission with extra
    /// steps.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_ohne_Begruendung_wird_abgelehnt(string leer)
    {
        var ohne = () => Zusammensetzen(noelia => noelia.UseDefaults().Without(NoeliaModule.Caching, leer));

        ohne.Should().Throw<ArgumentException>().WithMessage("*reason*");
    }

    [Fact]
    public void Use_ergaenzt_ein_Modul_das_nicht_in_der_Vorgabe_steht()
    {
        var mit = Zusammensetzen(noelia => noelia.UseDefaults().Use(NoeliaModule.TokenSessions));
        var ohne = Zusammensetzen(noelia => noelia.UseDefaults());

        ohne.Included.Should().NotContain(NoeliaModule.TokenSessions);
        mit.Included.Should().Contain(NoeliaModule.TokenSessions);
    }

    [Fact]
    public void Die_letzte_Nennung_gewinnt()
    {
        var wiederAufgenommen = Zusammensetzen(noelia => noelia
            .UseDefaults()
            .Without(NoeliaModule.Caching, "erst nicht")
            .Use(NoeliaModule.Caching));

        var doch_nicht = Zusammensetzen(noelia => noelia
            .UseDefaults()
            .Use(NoeliaModule.Caching)
            .Without(NoeliaModule.Caching, "am Ende nicht"));

        wiederAufgenommen.Included.Should().Contain(NoeliaModule.Caching);
        doch_nicht.Included.Should().NotContain(NoeliaModule.Caching);
    }

    /// <summary>
    /// Modules depend on one another, so the order they register in belongs to
    /// Noelia, not to the order the caller happened to write.
    /// </summary>
    [Fact]
    public void Die_Reihenfolge_der_Aufrufe_bestimmt_die_Registrierung_nicht()
    {
        var eine = Zusammensetzen(noelia => noelia.UseDefaults()
            .Use(NoeliaModule.TokenSessions).Use(NoeliaModule.PasswordHashing));
        var andere = Zusammensetzen(noelia => noelia.UseDefaults()
            .Use(NoeliaModule.PasswordHashing).Use(NoeliaModule.TokenSessions));

        eine.Included.Should().Equal(andere.Included);
    }

    /// <summary>
    /// The Entity Framework property: a provider extends the builder from
    /// outside, and the builder knows nothing about it.
    /// </summary>
    [Fact]
    public void Ein_Fremdmodul_wird_aufgenommen_ohne_dass_Noelia_es_kennt()
    {
        var gelaufen = false;

        var zusammensetzung = Zusammensetzen(noelia => noelia
            .UseDefaults()
            .Use(Erfunden, _ => gelaufen = true));

        zusammensetzung.Included.Should().Contain(Erfunden);
        gelaufen.Should().BeTrue();
    }

    [Fact]
    public void Ein_Fremdmodul_laesst_sich_wieder_abwaehlen()
    {
        var gelaufen = false;

        var zusammensetzung = Zusammensetzen(noelia => noelia
            .Use(Erfunden, _ => gelaufen = true)
            .Without(Erfunden, "doch kein Bedarf"));

        zusammensetzung.Included.Should().NotContain(Erfunden);
        gelaufen.Should().BeFalse("a module that was dropped must not have registered anything");
    }

    /// <summary>
    /// Foreign modules keep the order they were declared in, after the built-in
    /// ones — a provider usually replaces something Noelia already asked for.
    /// </summary>
    [Fact]
    public void Fremdmodule_laufen_nach_den_eigenen_und_in_ihrer_Reihenfolge()
    {
        var reihenfolge = new List<string>();

        Zusammensetzen(noelia => noelia
            .Use(Zweitens, _ => reihenfolge.Add("zweitens"))
            .Use(Erfunden, _ => reihenfolge.Add("erfunden")));

        reihenfolge.Should().Equal("zweitens", "erfunden");
    }

    /// <summary>
    /// The composition is readable afterwards, so a service can say at startup
    /// what it is running and what it left out.
    /// </summary>
    [Fact]
    public void Die_Zusammensetzung_steht_im_Behaelter()
    {
        var services = Dienste();
        services.AddNoelia(Konfiguration(), Umgebung(), "test-service", noelia => noelia
            .UseDefaults()
            .Without(NoeliaModule.Communication, "kein Broker"));

        var zusammensetzung = services.BuildServiceProvider().GetRequiredService<NoeliaComposition>();

        zusammensetzung.Excluded.Should().ContainKey(NoeliaModule.Communication);
    }

    private static NoeliaComposition Zusammensetzen(Action<NoeliaBuilder> konfigurieren)
    {
        var services = Dienste();
        services.AddNoelia(Konfiguration(), Umgebung(), "test-service", konfigurieren);
        return services.BuildServiceProvider().GetRequiredService<NoeliaComposition>();
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
