using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Herkunft;

/// <summary>
/// What the rate limiter may believe about who is calling.
/// </summary>
/// <remarks>
/// A brake that believes a header the caller sets is not a brake. These run
/// against a real counting store rather than a substitute: the question is not
/// whether a method was called but which key it was called with, and a
/// substitute answers the wrong one of those.
/// </remarks>
[Trait("Category", "Unit")]
public class GefaelschteHerkunftTests
{
    private readonly IDistributedRateLimitStore _store = new InMemoryRateLimitStore(
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<InMemoryRateLimitStore>.Instance);

    private static DistributedRateLimitingOptions Options(int perMinute = 1) => new()
    {
        RequestsPerMinute = perMinute,
        RequestsPerHour = 10_000,
        RequestsPerDay = 100_000,
        WhitelistedIps = [],
        WhitelistedUserIds = [],
        WhitelistedEndpoints = []
    };

    private DistributedRateLimitingMiddleware Bremse(DistributedRateLimitingOptions options) =>
        new(_ => Task.CompletedTask,
            _store,
            NullLogger<DistributedRateLimitingMiddleware>.Instance,
            Microsoft.Extensions.Options.Options.Create(options));

    private static DefaultHttpContext Anfrage(string verbindung, string? gefaelscht = null, string? real = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(verbindung);

        if (gefaelscht is not null)
        {
            context.Request.Headers["X-Forwarded-For"] = gefaelscht;
        }

        if (real is not null)
        {
            context.Request.Headers["X-Real-IP"] = real;
        }

        return context;
    }

    /// <summary>
    /// The whole point. A caller who changes the header must not get a fresh
    /// allowance for doing so.
    /// </summary>
    [Fact]
    public async Task Ein_wechselnder_Kopf_verschafft_kein_neues_Kontingent()
    {
        var bremse = Bremse(Options(perMinute: 1));

        var erste = Anfrage("10.0.0.1", gefaelscht: "1.1.1.1");
        await bremse.InvokeAsync(erste);
        erste.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        var zweite = Anfrage("10.0.0.1", gefaelscht: "2.2.2.2");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(
            StatusCodes.Status429TooManyRequests,
            "one connection is one caller, whatever it writes in a header");
    }

    /// <summary>
    /// The same for the second header, which is the second way in.
    /// </summary>
    [Fact]
    public async Task Auch_X_Real_IP_verschafft_kein_neues_Kontingent()
    {
        var bremse = Bremse(Options(perMinute: 1));

        await bremse.InvokeAsync(Anfrage("10.0.0.1", real: "1.1.1.1"));

        var zweite = Anfrage("10.0.0.1", real: "2.2.2.2");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// The sharpest form: the allowance list is read through the same header, so
    /// one line of forgery removes the brake entirely.
    /// </summary>
    /// <remarks>
    /// <c>WhitelistedIps</c> carries <c>127.0.0.1</c> by default, which is what
    /// makes this reachable without any configuration at all.
    /// </remarks>
    [Fact]
    public async Task Auf_die_Ausnahmeliste_kommt_man_nicht_durch_einen_Kopf()
    {
        var options = Options(perMinute: 1);
        options.WhitelistedIps = ["127.0.0.1"];
        var bremse = Bremse(options);

        await bremse.InvokeAsync(Anfrage("10.0.0.1", gefaelscht: "127.0.0.1"));

        var zweite = Anfrage("10.0.0.1", gefaelscht: "127.0.0.1");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(
            StatusCodes.Status429TooManyRequests,
            "the exemption belongs to the loopback connection, not to whoever names it");
    }

    /// <summary>
    /// And the list still works for the address it was written for.
    /// </summary>
    [Fact]
    public async Task Die_Ausnahmeliste_gilt_weiter_fuer_die_Verbindung_selbst()
    {
        var options = Options(perMinute: 1);
        options.WhitelistedIps = ["127.0.0.1"];
        var bremse = Bremse(options);

        await bremse.InvokeAsync(Anfrage("127.0.0.1"));

        var zweite = Anfrage("127.0.0.1");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    /// <summary>
    /// Two connections remain two callers — the fix must not collapse everyone
    /// into one bucket.
    /// </summary>
    [Fact]
    public async Task Zwei_Verbindungen_bleiben_zwei_Aufrufer()
    {
        var bremse = Bremse(Options(perMinute: 1));

        await bremse.InvokeAsync(Anfrage("10.0.0.1"));

        var andere = Anfrage("10.0.0.2");
        await bremse.InvokeAsync(andere);

        andere.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }
}
