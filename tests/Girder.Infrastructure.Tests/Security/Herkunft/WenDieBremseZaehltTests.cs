using System.Security.Claims;
using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Security.Herkunft;

/// <summary>
/// Whom the brake counts, when the application has said whom it wants counted.
/// </summary>
/// <remarks>
/// Against a real counting store: the setting is only honoured if two requests
/// the application considers one caller land on one key, and only the store can
/// say whether they did.
/// </remarks>
[Trait("Category", "Unit")]
public class WenDieBremseZaehltTests
{
    private readonly IDistributedRateLimitStore _store = new InMemoryRateLimitStore(
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<InMemoryRateLimitStore>.Instance);

    private static DistributedRateLimitingOptions Options(RateLimitSubject subject) => new()
    {
        Subject = subject,
        RequestsPerMinute = 1,
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

    private static DefaultHttpContext Anfrage(string herkunft, string? benutzer = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(herkunft);

        if (benutzer is not null)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim("sub", benutzer)], "test"));
        }

        return context;
    }

    /// <summary>
    /// The finding, as a test: asking for per-origin used to count per user.
    /// </summary>
    [Fact]
    public async Task Je_Herkunft_zaehlt_je_Herkunft_auch_bei_angemeldeten_Benutzern()
    {
        var bremse = Bremse(Options(RateLimitSubject.Origin));

        await bremse.InvokeAsync(Anfrage("10.0.0.1", benutzer: "anna"));

        var zweite = Anfrage("10.0.0.1", benutzer: "bert");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(
            StatusCodes.Status429TooManyRequests,
            "two accounts behind one address are one origin, which is what was asked for");
    }

    [Fact]
    public async Task Je_Benutzer_zaehlt_je_Benutzer_auch_ueber_Herkuenfte_hinweg()
    {
        var bremse = Bremse(Options(RateLimitSubject.User));

        await bremse.InvokeAsync(Anfrage("10.0.0.1", benutzer: "anna"));

        var zweite = Anfrage("10.0.0.2", benutzer: "anna");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    /// <summary>
    /// Per-user leaves everyone unauthenticated in one bucket, which is the safe
    /// direction and has to be said out loud.
    /// </summary>
    [Fact]
    public async Task Je_Benutzer_wirft_alle_Unangemeldeten_in_einen_Topf()
    {
        var bremse = Bremse(Options(RateLimitSubject.User));

        await bremse.InvokeAsync(Anfrage("10.0.0.1"));

        var zweite = Anfrage("10.0.0.2");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
    }

    [Fact]
    public async Task Die_Vorgabe_ist_Benutzer_und_sonst_Herkunft()
    {
        new DistributedRateLimitingOptions().Subject
            .Should().Be(RateLimitSubject.UserThenOrigin);
    }

    [Fact]
    public async Task Vorgabe_trennt_zwei_Benutzer_hinter_einer_Herkunft()
    {
        var bremse = Bremse(Options(RateLimitSubject.UserThenOrigin));

        await bremse.InvokeAsync(Anfrage("10.0.0.1", benutzer: "anna"));

        var zweite = Anfrage("10.0.0.1", benutzer: "bert");
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    /// <summary>
    /// A tenant, an API key, anything the application knows and Girder does not.
    /// </summary>
    [Fact]
    public async Task Ein_eigener_Auszug_wird_wirklich_gelesen()
    {
        var options = Options(RateLimitSubject.Custom);
        options.SubjectExtractor = context => context.Request.Headers["X-Tenant"].ToString();
        var bremse = Bremse(options);

        var erste = Anfrage("10.0.0.1");
        erste.Request.Headers["X-Tenant"] = "acme";
        await bremse.InvokeAsync(erste);

        var zweite = Anfrage("10.0.0.2");
        zweite.Request.Headers["X-Tenant"] = "acme";
        await bremse.InvokeAsync(zweite);

        zweite.Response.StatusCode.Should().Be(
            StatusCodes.Status429TooManyRequests,
            "one tenant across two addresses is one subject");
    }

    /// <summary>
    /// Naming a custom subject without supplying one is a configuration mistake
    /// that must not be answered by quietly counting something else.
    /// </summary>
    [Fact]
    public async Task Ein_eigener_Auszug_ohne_Auszug_wird_gemeldet()
    {
        var bremse = Bremse(Options(RateLimitSubject.Custom));

        var fehlt = async () => await bremse.InvokeAsync(Anfrage("10.0.0.1"));

        await fehlt.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*SubjectExtractor*");
    }
}
