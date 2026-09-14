using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.Builder;
using Noelia.InMemory.Hosting;
using Noelia.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Noelia.Infrastructure.Tests.Builder;

/// <summary>
/// The acceptance case: one call, and a service that runs.
/// </summary>
/// <remarks>
/// Every module in the default set has to be startable without anything else
/// being registered. A default that needs a second line is not a default, and
/// the failure would land at startup on somebody else's afternoon.
/// </remarks>
[Trait("Category", "Unit")]
public class EinDienstKommtHochTests
{
    [Fact]
    public async Task Ein_Dienst_kommt_mit_UseDefaults_hoch_ohne_weitere_Zeile()
    {
        using var host = await Hochfahren(noelia => noelia.UseDefaults());

        var antwort = await host.GetTestClient().GetAsync(new Uri("http://localhost/"));

        antwort.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// And with nothing chosen at all: no infrastructure, and no complaint
    /// about it either.
    /// </summary>
    [Fact]
    public async Task Ein_Dienst_kommt_auch_ganz_ohne_Noelia_hoch()
    {
        using var host = await Hochfahren(_ => { });

        var antwort = await host.GetTestClient().GetAsync(new Uri("http://localhost/"));

        antwort.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// The composition is available to the service that was composed, so it can
    /// say at startup what it is running.
    /// </summary>
    [Fact]
    public async Task Der_Dienst_kann_seine_eigene_Zusammensetzung_lesen()
    {
        using var host = await Hochfahren(noelia => noelia
            .UseDefaults()
            .Without(NoeliaModule.Communication, "kein Broker auf diesem Dienst"));

        var text = await (await host.GetTestClient().GetAsync(new Uri("http://localhost/zusammensetzung")))
            .Content.ReadAsStringAsync();

        text.Should().Contain("Communication: kein Broker auf diesem Dienst");
    }


    /// <summary>
    /// The Entity Framework property, across a package boundary: Noelia has
    /// never heard of Noelia.InMemory, and a composition can still name its
    /// modules.
    /// </summary>
    [Fact]
    public async Task Ein_Anbieterpaket_steuert_ein_Modul_bei_ohne_dass_Noelia_es_kennt()
    {
        using var host = await Hochfahren(noelia => noelia
            .UseDefaults()
            .Use(NoeliaModule.HttpResponseCaching)
            .UseInMemoryCache("test-service"));

        var antwort = await host.GetTestClient().GetAsync(new Uri("http://localhost/"));

        antwort.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// And the module that needs it says so by name when it is missing, rather
    /// than failing on whichever request reaches it first.
    /// </summary>
    [Fact]
    public async Task Ohne_den_Anbieter_verweigert_das_Modul_den_Start()
    {
        var ohne = async () => await Hochfahren(noelia => noelia
            .UseDefaults()
            .Use(NoeliaModule.HttpResponseCaching));

        (await ohne.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*IDistributedCacheService*");
    }

    private static async Task<IHost> Hochfahren(Action<NoeliaBuilder> konfigurieren) =>
        await new HostBuilder()
            // Every module has to register whole modules: a consumer whose
            // constructor argument nobody supplies must fail while the container
            // is built, not on the first request that happens to need it.
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            })
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureAppConfiguration(config => config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["JwtSettings:Issuer"] = "test",
                        ["JwtSettings:Audience"] = "test"
                    }))
                .ConfigureServices((context, services) =>
                    services.AddNoelia(
                        context.Configuration,
                        context.HostingEnvironment,
                        "test-service",
                        konfigurieren))
                .ConfigureServices(services => services.AddRouting())
                .Configure(app => app.Run(async http =>
                {
                    if (http.Request.Path == "/zusammensetzung")
                    {
                        var zusammensetzung = http.RequestServices.GetRequiredService<NoeliaComposition>();
                        await http.Response.WriteAsync(string.Join(
                            "\n", zusammensetzung.Excluded.Select(e => $"{e.Key}: {e.Value}")));
                        return;
                    }

                    await http.Response.WriteAsync("oben");
                })))
            .StartAsync();
}
