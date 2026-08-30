using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Http;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder;

/// <summary>
/// Settings for the rate limit, nested inside the composition the way a database
/// provider's options nest inside Entity Framework's.
/// </summary>
/// <remarks>
/// Everything here has a default that works. What it does not have is a default
/// that hides: the two settings that decide whether a limit is a limit at all —
/// whom it counts, and whom it believes about who that is — can both be written
/// down even when the value chosen is the default one.
/// </remarks>
public sealed class RateLimitingBuilder
{
    private readonly GirderBuilder _girder;
    private bool _trustDeclared;
    private bool _trustRefused;

    internal RateLimitingBuilder(GirderBuilder girder) => _girder = girder;

    /// <summary>
    /// Counts per origin, whether or not anyone is signed in.
    /// </summary>
    /// <remarks>
    /// What a sign-in route wants: counting per user cannot slow down guessing
    /// at users, because each guess is a different user.
    /// </remarks>
    public RateLimitingBuilder PerOrigin() => Subject(RateLimitSubject.Origin);

    /// <summary>
    /// Counts per signed-in user, with one shared allowance for everyone who is
    /// not signed in.
    /// </summary>
    public RateLimitingBuilder PerUser() => Subject(RateLimitSubject.User);

    /// <summary>
    /// Counts per user where there is one and per origin otherwise. The default.
    /// </summary>
    public RateLimitingBuilder PerUserThenOrigin() => Subject(RateLimitSubject.UserThenOrigin);

    /// <summary>
    /// Counts per whatever this returns — a tenant, an API key, something only
    /// the application knows.
    /// </summary>
    /// <remarks>
    /// Two requests it names alike share one allowance. Returning the same value
    /// for everyone puts everyone in one bucket, which is safe and almost never
    /// meant.
    /// </remarks>
    public RateLimitingBuilder PerSubject(Func<HttpContext, string> subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        _girder.Services.Configure<DistributedRateLimitingOptions>(options =>
        {
            options.Subject = RateLimitSubject.Custom;
            options.SubjectExtractor = subject;
        });

        return this;
    }

    /// <summary>
    /// Believes no forwarded header. The default, said out loud.
    /// </summary>
    /// <remarks>
    /// Worth writing even though it changes nothing: it tells the next reader
    /// that the service is meant to be reached directly, so putting a proxy in
    /// front of it later is a change to this line and not a silent one.
    /// </remarks>
    public RateLimitingBuilder TrustNoForwardedHeaders()
    {
        if (_trustDeclared)
        {
            throw new InvalidOperationException(
                "TrustNoForwardedHeaders() contradicts the proxies already declared. "
                + "Keep one of the two.");
        }

        _trustRefused = true;
        return this;
    }

    /// <summary>
    /// Believes <c>X-Forwarded-For</c> from these proxies, and from nobody else.
    /// </summary>
    /// <remarks>
    /// Without this the connection's own address is what counts, which behind a
    /// load balancer is the load balancer — one bucket for everyone. With it,
    /// the platform rewrites the peer address for requests that really arrived
    /// through a named proxy, and leaves every other request alone.
    /// </remarks>
    /// <param name="proxies">
    /// Addresses (<c>10.0.0.7</c>) or CIDR networks (<c>10.0.0.0/8</c>).
    /// </param>
    public RateLimitingBuilder TrustForwardedHeadersFrom(params string[] proxies)
    {
        if (_trustRefused)
        {
            throw new InvalidOperationException(
                "TrustForwardedHeadersFrom(...) contradicts TrustNoForwardedHeaders(). "
                + "Keep one of the two.");
        }

        _girder.Services.TrustForwardedHeadersFrom(proxies);
        _trustDeclared = true;
        return this;
    }

    /// <summary>How many requests one subject may make.</summary>
    /// <remarks>Zero on any window turns that window off.</remarks>
    public RateLimitingBuilder Allowing(int perMinute, int perHour, int perDay)
    {
        _girder.Services.Configure<DistributedRateLimitingOptions>(options =>
        {
            options.RequestsPerMinute = perMinute;
            options.RequestsPerHour = perHour;
            options.RequestsPerDay = perDay;
        });

        return this;
    }

    /// <summary>
    /// Origins that are not counted at all.
    /// </summary>
    /// <remarks>
    /// Matched against the address that connected, so an exemption cannot be
    /// claimed by asking for it in a header.
    /// </remarks>
    public RateLimitingBuilder Exempting(params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(origins);

        _girder.Services.Configure<DistributedRateLimitingOptions>(options =>
        {
            options.WhitelistedIps = [.. origins];
        });

        return this;
    }

    private RateLimitingBuilder Subject(RateLimitSubject subject)
    {
        _girder.Services.Configure<DistributedRateLimitingOptions>(options =>
        {
            options.Subject = subject;
            options.SubjectExtractor = null;
        });

        return this;
    }
}

/// <summary>Rate limiting, with its settings.</summary>
public static class RateLimitingModuleExtensions
{
    /// <summary>
    /// Sets up rate limiting and configures it.
    /// </summary>
    /// <remarks>
    /// Adds the module if it is not already in, so it can be written on its own
    /// rather than beside a <c>Use(GirderModule.RateLimiting)</c> that would say
    /// the same thing twice.
    /// </remarks>
    public static GirderBuilder UseRateLimiting(
        this GirderBuilder girder,
        Action<RateLimitingBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(girder);
        ArgumentNullException.ThrowIfNull(configure);

        girder.Use(GirderModule.RateLimiting);
        configure(new RateLimitingBuilder(girder));
        return girder;
    }
}
