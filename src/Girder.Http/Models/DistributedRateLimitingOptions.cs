using Microsoft.AspNetCore.Http;

namespace Girder.Infrastructure.Models;

/// <summary>
/// Configuration options for distributed rate limiting
/// </summary>
public class DistributedRateLimitingOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "DistributedRateLimiting";

    /// <summary>
    /// Whether rate limiting is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Requests per minute on every path that has no entry of its own.
    /// </summary>
    /// <remarks>
    /// <para><strong>Zero turns it off, and that is how you brake a named set of
    /// paths and nothing else.</strong> A limit of zero writes no counter, and a
    /// request counted against nothing is allowed — so with all three defaults at
    /// zero, only <see cref="EndpointSpecificLimits"/> decides:</para>
    /// <code>
    /// "DistributedRateLimiting": {
    ///   "RequestsPerMinute": 0, "RequestsPerHour": 0, "RequestsPerDay": 0,
    ///   "EndpointSpecificLimits": { "/auth/login": { "RequestsPerMinute": 20 } }
    /// }
    /// </code>
    /// <para>That shape is what a gateway needs: the whole user interface travels
    /// through it, and a default limit over everything would count each asset
    /// fetch. It was always possible and never said, so it got rebuilt by hand
    /// instead — twice, in one application.</para>
    /// </remarks>
    public int RequestsPerMinute { get; set; } = 100;

    /// <summary>
    /// Requests per hour on every path that has no entry of its own. Zero turns
    /// it off — see <see cref="RequestsPerMinute"/>.
    /// </summary>
    public int RequestsPerHour { get; set; } = 1000;

    /// <summary>
    /// Requests per day on every path that has no entry of its own. Zero turns
    /// it off — see <see cref="RequestsPerMinute"/>.
    /// </summary>
    public int RequestsPerDay { get; set; } = 10000;

    /// <summary>
    /// Multiplies every limit. For an environment where a test suite hammers
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para><strong>A factor and not a switch, on purpose.</strong> With a
    /// factor the limiter still runs: it counts, it keys per subject, it answers
    /// with its headers — only the ceiling is higher. A limiter switched off is
    /// invisible in the one environment that runs continuously, and what is never
    /// seen is not noticed when it breaks.</para>
    /// <para>Belongs in the environment that needs it and nowhere else. In
    /// staging it would be a limiter that only looks like one.</para>
    /// </remarks>
    public int LimitMultiplier { get; set; } = 1;

    /// <summary>
    /// Whom a request counts against.
    /// </summary>
    /// <remarks>
    /// Read on every request. The pair of booleans this replaced could say the
    /// same thing twice and disagree, and only one of the two was ever read.
    /// </remarks>
    public RateLimitSubject Subject { get; set; } = RateLimitSubject.UserThenOrigin;

    /// <summary>
    /// Names the caller when <see cref="RateLimitSubject.Custom"/> is chosen —
    /// a tenant, an API key, anything the application knows and Girder does not.
    /// </summary>
    /// <remarks>
    /// Whatever it returns is the whole identity: two requests it names alike
    /// share one allowance. Returning an empty string for some callers puts all
    /// of them in one bucket, which is safe but rarely meant.
    /// </remarks>
    public Func<HttpContext, string>? SubjectExtractor { get; set; }

    /// <summary>
    /// Whether to enable endpoint-specific rate limiting
    /// </summary>
    public bool EnableEndpointSpecificLimiting { get; set; } = true;

    /// <summary>
    /// Whether to use sliding window algorithm (true) or fixed window (false)
    /// </summary>
    public bool UseSlidingWindow { get; set; } = true;

    /// <summary>
    /// Origins that are never counted. Empty.
    /// </summary>
    /// <remarks>
    /// <para><strong>It used to hold loopback, and that could not be undone from
    /// configuration.</strong> The .NET configuration binder <em>adds</em> to a
    /// collection and never replaces it, and an empty JSON array is
    /// indistinguishable from an absent key — measured: writing
    /// <c>"WhitelistedIps": ["9.9.9.9"]</c> produces
    /// <c>127.0.0.1, ::1, 9.9.9.9</c>. So an operator who wrote the list out
    /// deliberately still got the exemption, and no reading of their own
    /// configuration would tell them.</para>
    ///
    /// <para>A default nobody can remove is a trap, and this one hides in the
    /// place a rate limit is first tried out — your own machine, where it then
    /// looks as though nothing counts. Say what you want exempt; nothing is
    /// exempt by default.</para>
    /// </remarks>
    public HashSet<string> WhitelistedIps { get; set; } = [];

    /// <summary>
    /// Subjects that are never counted. Empty, for the reason given on
    /// <see cref="WhitelistedIps"/>.
    /// </summary>
    public HashSet<string> WhitelistedUserIds { get; set; } = [];

    /// <summary>
    /// Endpoints that are never counted. Empty.
    /// </summary>
    /// <remarks>
    /// It used to hold the health paths, and could not be cleared either. The
    /// probe is protected by <em>order</em> instead, which is the honest place
    /// for it: <c>UseHealthCheckEndpoints()</c> runs before
    /// <c>UseRateLimiting()</c> in the default chain, so a liveness probe never
    /// reaches the limiter at all. Relying on a whitelist entry meant relying on
    /// a path string matching, on a chain that ran the limiter first.
    /// </remarks>
    public HashSet<string> WhitelistedEndpoints { get; set; } = [];

    /// <summary>
    /// Per-path limits, and empty until an application names its own.
    /// </summary>
    /// <remarks>
    /// <para>It used to arrive holding seven paths from the application Girder
    /// was extracted from — <c>/api/auth/login</c>, <c>/api/admin/*</c> and the
    /// rest. Configuration <em>adds</em> to this dictionary rather than replacing
    /// it, so those paths could not be removed from outside; measured, a call to
    /// <c>POST /api/auth/register</c> was refused at three per minute in an
    /// application that has no such route, and would have been silently
    /// mis-limited in one that has it under a different meaning.</para>
    ///
    /// <para>A library must not carry one application's map. What the paths were
    /// worth is the shape, and that is in the README.</para>
    /// </remarks>
    public Dictionary<string, EndpointRateLimit> EndpointSpecificLimits { get; set; } = [];

    /// <summary>
    /// Redis connection configuration
    /// </summary>
    public RedisRateLimitingOptions Redis { get; set; } = new();

    /// <summary>
    /// Circuit breaker configuration for rate limiting
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();
}

/// <summary>
/// Redis-specific configuration for rate limiting
/// </summary>
public class RedisRateLimitingOptions
{
    /// <summary>
    /// Redis connection string
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Redis database number to use for rate limiting
    /// </summary>
    public int Database { get; set; } = 1;

    /// <summary>
    /// Key prefix for rate limiting keys
    /// </summary>
    public string KeyPrefix { get; set; } = "rl:";

    /// <summary>
    /// Connection timeout in milliseconds
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// Command timeout in milliseconds
    /// </summary>
    public int CommandTimeout { get; set; } = 1000;

    /// <summary>
    /// Number of retry attempts for failed Redis operations
    /// </summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Whether to use Redis clustering
    /// </summary>
    public bool UseCluster { get; set; } = false;

    /// <summary>
    /// SSL configuration for Redis connection
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Password for Redis authentication
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// Circuit breaker configuration for rate limiting resilience
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Whether circuit breaker is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of failures before opening the circuit
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Time to wait before attempting to close the circuit
    /// </summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for individual Redis operations
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Fallback behavior when circuit is open
    /// </summary>
    public CircuitBreakerFallback FallbackBehavior { get; set; } = CircuitBreakerFallback.AllowAll;
}

/// <summary>
/// Fallback behavior when Redis is unavailable
/// </summary>
public enum CircuitBreakerFallback
{
    /// <summary>
    /// Allow all requests through
    /// </summary>
    AllowAll,

    /// <summary>
    /// Deny all requests
    /// </summary>
    DenyAll,

    /// <summary>
    /// Use in-memory fallback
    /// </summary>
    UseInMemory
}

/// <summary>
/// Whom a rate limit counts.
/// </summary>
/// <remarks>
/// The choice decides who pays for whom. Per user, one account cannot exhaust
/// another's allowance but a single machine may open many accounts; per origin,
/// one office behind one address shares one allowance. Neither is right for
/// every route, which is why it is a setting and not a default nobody sees.
/// </remarks>
public enum RateLimitSubject
{
    /// <summary>
    /// The signed-in user, and the origin for everyone else. The usual answer.
    /// </summary>
    UserThenOrigin,

    /// <summary>
    /// The origin, whether or not anyone is signed in. What a sign-in route
    /// wants: counting per user cannot slow down guessing at users.
    /// </summary>
    Origin,

    /// <summary>
    /// The signed-in user, with one shared allowance for everyone who is not.
    /// </summary>
    User,

    /// <summary>
    /// Whatever <see cref="DistributedRateLimitingOptions.SubjectExtractor"/>
    /// returns.
    /// </summary>
    Custom
}
