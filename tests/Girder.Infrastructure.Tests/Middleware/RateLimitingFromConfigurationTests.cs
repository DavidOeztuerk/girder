using System.Net;
using Girder.Abstractions.Caching;
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
/// Everything a rate limit needs to be, said in configuration rather than code.
/// </summary>
/// <remarks>
/// <para>An application that cannot express its shape here writes its own
/// limiter, and then maintains it forever. That happened: one gateway needed to
/// brake five named paths and leave everything else alone, concluded this was
/// impossible, and rebuilt the middleware by hand.</para>
/// <para>It was always possible. Nothing said so and no test held it, which for
/// a library is the same as not offering it.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class RateLimitingFromConfigurationTests
{
    /// <summary>
    /// Defaults at zero: only the named paths are counted.
    /// </summary>
    /// <remarks>
    /// The shape a gateway needs — the whole user interface travels through it,
    /// so a default over everything would count each asset fetch.
    /// </remarks>
    [Fact]
    public async Task With_the_defaults_at_zero_only_the_named_path_is_braked()
    {
        using var host = await Start(new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 0,
            RequestsPerHour = 0,
            RequestsPerDay = 0,
            Subject = RateLimitSubject.Origin,
            WhitelistedIps = [],
            EndpointSpecificLimits = { ["/auth/login"] = new EndpointRateLimit { RequestsPerMinute = 2 } }
        });

        var client = host.GetTestClient();

        // Ten calls on a path nobody named — all of them get through.
        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync(new Uri("http://localhost/jobs"))).StatusCode
                .Should().Be(HttpStatusCode.OK, "no limit was set for this path");
        }

        (await client.GetAsync(new Uri("http://localhost/auth/login"))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("http://localhost/auth/login"))).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("http://localhost/auth/login"))).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "the named path counts");
    }

    /// <summary>
    /// The counter-probe: leave the defaults alone and the same unnamed path is
    /// counted after all.
    /// </summary>
    [Fact]
    public async Task With_a_default_in_place_the_unnamed_path_is_braked_too()
    {
        using var host = await Start(new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 2,
            RequestsPerHour = 0,
            RequestsPerDay = 0,
            Subject = RateLimitSubject.Origin,
            WhitelistedIps = []
        });

        var client = host.GetTestClient();

        await client.GetAsync(new Uri("http://localhost/jobs"));
        await client.GetAsync(new Uri("http://localhost/jobs"));

        (await client.GetAsync(new Uri("http://localhost/jobs"))).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests);
    }

    /// <summary>
    /// The multiplier lifts the ceiling and leaves the limiter running.
    /// </summary>
    [Fact]
    public async Task The_multiplier_lifts_the_ceiling()
    {
        using var host = await Start(new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 2,
            RequestsPerHour = 0,
            RequestsPerDay = 0,
            LimitMultiplier = 3,
            Subject = RateLimitSubject.Origin,
            WhitelistedIps = []
        });

        var client = host.GetTestClient();

        for (var i = 0; i < 6; i++)
        {
            (await client.GetAsync(new Uri("http://localhost/jobs"))).StatusCode
                .Should().Be(HttpStatusCode.OK, "two times three is six");
        }

        (await client.GetAsync(new Uri("http://localhost/jobs"))).StatusCode
            .Should().Be(HttpStatusCode.TooManyRequests, "the seventh is over the line");
    }

    /// <summary>
    /// A multiplier must not turn an off switch into a small limit.
    /// </summary>
    [Fact]
    public async Task The_multiplier_leaves_a_zero_off()
    {
        using var host = await Start(new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 0,
            RequestsPerHour = 0,
            RequestsPerDay = 0,
            LimitMultiplier = 5,
            Subject = RateLimitSubject.Origin,
            WhitelistedIps = []
        });

        var client = host.GetTestClient();

        for (var i = 0; i < 12; i++)
        {
            (await client.GetAsync(new Uri("http://localhost/jobs"))).StatusCode
                .Should().Be(HttpStatusCode.OK, "zero times five is still off, not five");
        }
    }

    /// <summary>
    /// The refusal carries the two headers that matter for a body a browser gets.
    /// </summary>
    /// <remarks>
    /// This middleware writes the response and returns, so nothing further down
    /// the chain reaches it. A service running the limiter without the
    /// security-header module got a refusal with none at all.
    /// </remarks>
    [Fact]
    public async Task The_refusal_carries_its_own_security_headers()
    {
        using var host = await Start(new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 1,
            RequestsPerHour = 0,
            RequestsPerDay = 0,
            Subject = RateLimitSubject.Origin,
            WhitelistedIps = []
        });

        var client = host.GetTestClient();
        await client.GetAsync(new Uri("http://localhost/jobs"));

        var refused = await client.GetAsync(new Uri("http://localhost/jobs"));

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        refused.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
    }

    /// <summary>
    /// The refusal carries the limit headers too.
    /// </summary>
    /// <remarks>
    /// They used to go on the allowed answer only, so the one response where a
    /// caller most needs to read the limit — and see that nothing is left — was
    /// the one without them.
    /// </remarks>
    [Fact]
    public async Task The_refusal_carries_the_limit_headers()
    {
        using var host = await Start(new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 1,
            RequestsPerHour = 0,
            RequestsPerDay = 0,
            Subject = RateLimitSubject.Origin,
            WhitelistedIps = []
        });

        var client = host.GetTestClient();
        await client.GetAsync(new Uri("http://localhost/jobs"));

        var refused = await client.GetAsync(new Uri("http://localhost/jobs"));

        refused.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        refused.Headers.GetValues("X-RateLimit-Limit").Should().Equal("1");
        refused.Headers.GetValues("X-RateLimit-Remaining").Should().Equal("0");
        refused.Headers.GetValues("Retry-After").Should().Equal("60");
    }

    private static async Task<IHost> Start(DistributedRateLimitingOptions options) =>
        await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddMemoryCache();
                    services.AddSingleton<IDistributedRateLimitStore>(provider =>
                        new InProcessRateLimitStore(provider.GetRequiredService<IMemoryCache>()));
                    services.AddSingleton(Options.Create(options));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<DistributedRateLimitingMiddleware>();
                    app.Run(http => http.Response.WriteAsync("through"));
                }))
            .StartAsync();
}
