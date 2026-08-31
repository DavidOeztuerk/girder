using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Builder;
using Girder.InMemory.Hosting;
using Girder.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Builder;

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
        using var host = await Hochfahren(girder => girder.UseDefaults());

        var antwort = await host.GetTestClient().GetAsync(new Uri("http://localhost/"));

        antwort.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// And with nothing chosen at all: no infrastructure, and no complaint
    /// about it either.
    /// </summary>
    [Fact]
    public async Task Ein_Dienst_kommt_auch_ganz_ohne_Girder_hoch()
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
        using var host = await Hochfahren(girder => girder
            .UseDefaults()
            .Without(GirderModule.Communication, "kein Broker auf diesem Dienst"));

        var text = await (await host.GetTestClient().GetAsync(new Uri("http://localhost/zusammensetzung")))
            .Content.ReadAsStringAsync();

        text.Should().Contain("Communication: kein Broker auf diesem Dienst");
    }


    /// <summary>
    /// The Entity Framework property, across a package boundary: Girder has
    /// never heard of Girder.InMemory, and a composition can still name its
    /// modules.
    /// </summary>
    [Fact]
    public async Task Ein_Anbieterpaket_steuert_ein_Modul_bei_ohne_dass_Girder_es_kennt()
    {
        using var host = await Hochfahren(girder => girder
            .UseDefaults()
            .Use(GirderModule.HttpResponseCaching)
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
        var ohne = async () => await Hochfahren(girder => girder
            .UseDefaults()
            .Use(GirderModule.HttpResponseCaching));

        (await ohne.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*IDistributedCacheService*");
    }

    private static async Task<IHost> Hochfahren(Action<GirderBuilder> konfigurieren) =>
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
                    services.AddGirder(
                        context.Configuration,
                        context.HostingEnvironment,
                        "test-service",
                        konfigurieren))
                .ConfigureServices(services => services.AddRouting())
                .Configure(app => app.Run(async http =>
                {
                    if (http.Request.Path == "/zusammensetzung")
                    {
                        var zusammensetzung = http.RequestServices.GetRequiredService<GirderComposition>();
                        await http.Response.WriteAsync(string.Join(
                            "\n", zusammensetzung.Excluded.Select(e => $"{e.Key}: {e.Value}")));
                        return;
                    }

                    await http.Response.WriteAsync("oben");
                })))
            .StartAsync();
}
