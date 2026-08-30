using System.Diagnostics;
using Girder.Abstractions.Observability;
using Girder.Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Observability;

/// <summary>
/// Whether a correlation id survives a call to another service.
/// </summary>
/// <remarks>
/// One request usually touches several services, and the id is what stitches
/// their logs back into one story. It has to travel by default: a service that
/// has to remember to forward it forgets on exactly the path nobody tested.
/// </remarks>
[Trait("Category", "Unit")]
public class KorrelationUeberEinenSprungTests : IDisposable
{
    private readonly ActivitySource _source = new("Girder.Tests.Korrelation");
    private readonly ActivityListener _listener;

    public KorrelationUeberEinenSprungTests()
    {
        // Without a listener no Activity is created, and baggage has nowhere to live.
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Girder.Tests.Korrelation",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>
    /// A tag stays on the span it was written to. Baggage is what crosses a
    /// process boundary, and baggage is what the logging behaviour reads.
    /// </summary>
    [Fact]
    public async Task Die_Kennung_steht_als_Gepaeck_und_nicht_nur_als_Etikett()
    {
        using var activity = _source.StartActivity("anfrage");
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = "abc-123";

        await middleware.InvokeAsync(context);

        Activity.Current?.GetBaggageItem(CorrelationId.BaggageKey).Should().Be("abc-123");
    }

    [Fact]
    public async Task Die_Kennung_bleibt_auch_als_Etikett_am_Span()
    {
        using var activity = _source.StartActivity("anfrage");
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = "abc-123";

        await middleware.InvokeAsync(context);

        activity!.GetTagItem("correlation.id").Should().Be("abc-123");
    }

    /// <summary>
    /// The acceptance case: a bare HttpClient, nothing wired by hand.
    /// </summary>
    [Fact]
    public async Task Eine_Kennung_ueberlebt_einen_Sprung_mit_blankem_HttpClient()
    {
        var mitgereist = new List<string?>();

        using var host = await HostMit(mitgereist);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, "durchgereicht-1");

        await client.GetAsync(new Uri("http://localhost/"));

        mitgereist.Should().ContainSingle().Which.Should().Be("durchgereicht-1");
    }

    /// <summary>
    /// A caller who set the header meant it. Nothing overwrites it.
    /// </summary>
    [Fact]
    public async Task Eine_selbst_gesetzte_Kennung_wird_nicht_ueberschrieben()
    {
        var mitgereist = new List<string?>();

        using var host = await HostMit(mitgereist, eigeneKennung: "von-hand");
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add(CorrelationId.HeaderName, "durchgereicht-1");

        await client.GetAsync(new Uri("http://localhost/"));

        mitgereist.Should().ContainSingle().Which.Should().Be("von-hand");
    }

    /// <summary>
    /// Outside a request there is nothing to correlate, and inventing an id per
    /// outgoing call would produce a story with one sentence per line.
    /// </summary>
    [Fact]
    public async Task Ohne_laufende_Anfrage_wird_nichts_erfunden()
    {
        var mitgereist = new List<string?>();
        var handler = new CorrelationIdHandler { InnerHandler = new Mitschreiber(mitgereist) };
        using var client = new HttpClient(handler);

        await client.GetAsync(new Uri("http://dienst/etwas"));

        mitgereist.Should().ContainSingle().Which.Should().BeNull();
    }

    /// <summary>Builds a service that answers by calling another one.</summary>
    private static async Task<IHost> HostMit(List<string?> mitgereist, string? eigeneKennung = null)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddCorrelationIdPropagation();
                    services.AddHttpClient("weiter")
                        .ConfigurePrimaryHttpMessageHandler(() => new Mitschreiber(mitgereist));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.Run(async context =>
                    {
                        var factory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
                        using var weiter = factory.CreateClient("weiter");

                        using var anfrage = new HttpRequestMessage(HttpMethod.Get, new Uri("http://andere/"));
                        if (eigeneKennung is not null)
                        {
                            anfrage.Headers.Add(CorrelationId.HeaderName, eigeneKennung);
                        }

                        await weiter.SendAsync(anfrage);
                        await context.Response.WriteAsync("fertig");
                    });
                }))
            .StartAsync();

        return host;
    }

    /// <summary>Records what the outgoing request carried.</summary>
    private sealed class Mitschreiber(List<string?> gesehen) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            gesehen.Add(request.Headers.TryGetValues(CorrelationId.HeaderName, out var werte)
                ? werte.FirstOrDefault()
                : null);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
        GC.SuppressFinalize(this);
    }
}
