using System.Net;
using System.Text.Json;
using Girder.Abstractions.Caching;
using Girder.Abstractions.Observability;
using Girder.Infrastructure.Http;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Middleware;

/// <summary>
/// The refusal has the same shape as every other error, and names an id somebody
/// can quote.
/// </summary>
/// <remarks>
/// This is the answer a person is most likely to report — <em>I am locked
/// out</em> — and it was the one answer carrying no correlation id and declaring
/// itself ordinary JSON. A caller that separates errors from payload by content
/// type read it as payload.
/// </remarks>
[Trait("Category", "Unit")]
public class RateLimitRefusalIsAProblemDocumentTests
{
    [Fact]
    public async Task The_refusal_is_a_problem_document()
    {
        var (status, contentType, _) = await UntilRefused();

        status.Should().Be(HttpStatusCode.TooManyRequests);
        contentType.Should().StartWith("application/problem+json");
    }

    [Fact]
    public async Task The_refusal_names_the_correlation_id()
    {
        var (_, _, body) = await UntilRefused();

        JsonDocument.Parse(body).RootElement.GetProperty("correlationId").GetString()
            .Should().Be(
                "chain-from-the-caller",
                "whoever reports being locked out has to be able to name it");
    }

    /// <summary>
    /// <c>traceId</c> stays. It was the only id before, so someone is reading it.
    /// </summary>
    [Fact]
    public async Task The_trace_identifier_stays_alongside()
    {
        var (_, _, body) = await UntilRefused();

        JsonDocument.Parse(body).RootElement.TryGetProperty("traceId", out var trace)
            .Should().BeTrue();
        trace.GetString().Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Two spellings of one address are one bucket. Which spelling arrives is the
    /// listener's business, not the caller's — so leaving them apart handed a
    /// dual-stack caller twice the allowance.
    /// </summary>
    [Fact]
    public void One_address_in_two_spellings_is_one_origin()
    {
        var fourOnly = new DefaultHttpContext();
        fourOnly.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");

        var mapped = new DefaultHttpContext();
        mapped.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("::ffff:10.0.0.1");

        ClientAddress.Of(mapped).Should().Be(ClientAddress.Of(fourOnly));
    }

    /// <summary>
    /// The dictionary of per-path limits starts empty: a library must not carry
    /// one application's route map.
    /// </summary>
    /// <remarks>
    /// Configuration adds to it rather than replacing it, so the seven paths it
    /// used to hold could not be removed from outside — measured, a call to
    /// <c>POST /api/auth/register</c> was refused at three per minute in an
    /// application with no such route.
    /// </remarks>
    [Fact]
    public void The_per_path_limits_are_empty_until_someone_names_their_own()
    {
        new DistributedRateLimitingOptions().EndpointSpecificLimits.Should().BeEmpty();
    }

    private static async Task<(HttpStatusCode Status, string ContentType, string Body)> UntilRefused()
    {
        using var host = await Start();
        var client = host.GetTestClient();

        HttpResponseMessage? response = null;

        // Four calls against a limit of two: the last one has to be refused.
        for (var i = 0; i < 4; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("http://localhost/"));
            request.Headers.Add(CorrelationId.HeaderName, "chain-from-the-caller");

            response?.Dispose();
            response = await client.SendAsync(request);
        }

        using var last = response!;
        return (
            last.StatusCode,
            last.Content.Headers.ContentType?.ToString() ?? "",
            await last.Content.ReadAsStringAsync());
    }

    private static async Task<IHost> Start() =>
        await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddMemoryCache();
                    services.AddSingleton<IDistributedRateLimitStore>(sp =>
                        new InProcessRateLimitStore(sp.GetRequiredService<IMemoryCache>()));
                    services.AddSingleton(Options.Create(new DistributedRateLimitingOptions
                    {
                        RequestsPerMinute = 2,
                        RequestsPerHour = 2,
                        RequestsPerDay = 2,
                        Subject = RateLimitSubject.Origin,

                        // Everything comes from loopback in a test host, and
                        // loopback is exempt by default — without this line the
                        // probe would measure a brake that never counts.
                        WhitelistedIps = []
                    }));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.UseMiddleware<DistributedRateLimitingMiddleware>();
                    app.Run(http => http.Response.WriteAsync("through"));
                }))
            .StartAsync();
}
