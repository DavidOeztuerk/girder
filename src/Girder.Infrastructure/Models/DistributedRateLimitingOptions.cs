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
    /// Default requests per minute limit
    /// </summary>
    public int RequestsPerMinute { get; set; } = 100;

    /// <summary>
    /// Default requests per hour limit
    /// </summary>
    public int RequestsPerHour { get; set; } = 1000;

    /// <summary>
    /// Default requests per day limit
    /// </summary>
    public int RequestsPerDay { get; set; } = 10000;

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
    /// IP addresses that are whitelisted from rate limiting
    /// </summary>
    public HashSet<string> WhitelistedIps { get; set; } = new()
    {
        "127.0.0.1",
        "::1"
    };

    /// <summary>
    /// User IDs that are whitelisted from rate limiting
    /// </summary>
    public HashSet<string> WhitelistedUserIds { get; set; } = new();

    /// <summary>
    /// Endpoints that are whitelisted from rate limiting
    /// </summary>
    public HashSet<string> WhitelistedEndpoints { get; set; } = new()
    {
        "GET:/health",
        "GET:/ready",
        "GET:/metrics"
    };

    /// <summary>
    /// Endpoint-specific rate limits
    /// </summary>
    public Dictionary<string, EndpointRateLimit> EndpointSpecificLimits { get; set; } = new()
    {
        // Authentication endpoints (more restrictive)
        { "/api/auth/login", new EndpointRateLimit { RequestsPerMinute = 5, RequestsPerHour = 20, RequestsPerDay = 100 } },
        { "/api/auth/register", new EndpointRateLimit { RequestsPerMinute = 3, RequestsPerHour = 10, RequestsPerDay = 50 } },
        { "/api/auth/forgot-password", new EndpointRateLimit { RequestsPerMinute = 2, RequestsPerHour = 5, RequestsPerDay = 10 } },

        // Public contact form (anti-spam)
        { "/api/contact", new EndpointRateLimit { RequestsPerMinute = 3, RequestsPerHour = 10, RequestsPerDay = 30 } },
        
        // File upload endpoints (more restrictive)
        { "/api/upload/*", new EndpointRateLimit { RequestsPerMinute = 10, RequestsPerHour = 50, RequestsPerDay = 200 } },
        
        // Search endpoints (moderate restrictions)
        { "/api/search/*", new EndpointRateLimit { RequestsPerMinute = 50, RequestsPerHour = 500, RequestsPerDay = 2000 } },
        
        // Admin endpoints — must accommodate normal admin navigation (16+ pages, multiple API calls per page).
        // Mutations (POST/PUT/DELETE) are further constrained by backend-level rate limiting.
        { "/api/admin/*", new EndpointRateLimit { RequestsPerMinute = 60, RequestsPerHour = 600, RequestsPerDay = 3000 } }
    };

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
