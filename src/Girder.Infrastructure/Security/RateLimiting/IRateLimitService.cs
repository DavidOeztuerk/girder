namespace Infrastructure.Security.RateLimiting;

/// <summary>
/// Interface for advanced rate limiting service
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// Check if request is allowed under rate limits
    /// </summary>
    Task<RateLimitResult> CheckRateLimitAsync(
        RateLimitRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Register a rate limit rule
    /// </summary>
    Task RegisterRuleAsync(
        RateLimitRule rule,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a rate limit rule
    /// </summary>
    Task RemoveRuleAsync(
        string ruleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current rate limit status for a client
    /// </summary>
    Task<RateLimitStatus> GetStatusAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset rate limits for a client
    /// </summary>
    Task ResetLimitsAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get rate limit statistics
    /// </summary>
    Task<RateLimitStatistics> GetStatisticsAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Add or update client-specific rate limits
    /// </summary>
    Task SetClientLimitsAsync(
        string clientId,
        Dictionary<string, RateLimitConfiguration> limits,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whitelist a client (bypass rate limits)
    /// </summary>
    Task WhitelistClientAsync(
        string clientId,
        TimeSpan? duration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Blacklist a client (block all requests)
    /// </summary>
    Task BlacklistClientAsync(
        string clientId,
        TimeSpan? duration = null,
        string? reason = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve all registered rate limit rules
    /// </summary>
    IEnumerable<RateLimitRule> GetRegisteredRules();
}

/// <summary>
/// Rate limit request context
/// </summary>
public class RateLimitRequest
{
    /// <summary>
    /// Client identifier (IP, User ID, API Key, etc.)
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// User ID (if authenticated)
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// User roles
    /// </summary>
    public List<string> UserRoles { get; set; } = new();

    /// <summary>
    /// API endpoint being accessed
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Request path
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Client IP address
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Request size in bytes
    /// </summary>
    public long? RequestSize { get; set; }

    /// <summary>
    /// API key used
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

/// <summary>
/// Rate limit check result
/// </summary>
public class RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// Rule that was triggered (if blocked)
    /// </summary>
    public RateLimitRule? TriggeredRule { get; set; }

    /// <summary>
    /// Current request count in the window
    /// </summary>
    public long CurrentCount { get; set; }

    /// <summary>
    /// Maximum allowed requests in the window
    /// </summary>
    public long Limit { get; set; }

    /// <summary>
    /// Remaining requests in the current window
    /// </summary>
    public long Remaining { get; set; }

    /// <summary>
    /// When the current window resets
    /// </summary>
    public DateTime ResetTime { get; set; }

    /// <summary>
    /// Retry after duration (if blocked)
    /// </summary>
    public TimeSpan? RetryAfter { get; set; }

    /// <summary>
    /// Response headers to include
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Reason for rate limiting
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Severity of the rate limit violation
    /// </summary>
    public RateLimitSeverity Severity { get; set; } = RateLimitSeverity.Normal;
}

/// <summary>
/// Rate limit rule definition
/// </summary>
public class RateLimitRule
{
    /// <summary>
    /// Unique rule identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Rule name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Rule description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether the rule is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Rule priority (higher number = higher priority)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Rate limit configuration
    /// </summary>
    public RateLimitConfiguration Configuration { get; set; } = new();

    /// <summary>
    /// Conditions for applying this rule
    /// </summary>
    public RateLimitConditions Conditions { get; set; } = new();

    /// <summary>
    /// Actions to take when limit is exceeded
    /// </summary>
    public RateLimitActions Actions { get; set; } = new();

    /// <summary>
    /// When this rule was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this rule was last modified
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Rule expiration time
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Tags for categorizing rules
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Rate limit configuration
/// </summary>
public class RateLimitConfiguration
{
    /// <summary>
    /// Maximum number of requests
    /// </summary>
    public long RequestLimit { get; set; } = 100;

    /// <summary>
    /// Time window for the limit
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Rate limiting algorithm
    /// </summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.SlidingWindow;

    /// <summary>
    /// Burst allowance (for token bucket)
    /// </summary>
    public long? BurstLimit { get; set; }

    /// <summary>
    /// Token refill rate (for token bucket)
    /// </summary>
    public double? RefillRate { get; set; }

    /// <summary>
    /// Penalty factor for repeated violations
    /// </summary>
    public double PenaltyFactor { get; set; } = 1.0;

    /// <summary>
    /// Maximum penalty duration
    /// </summary>
    public TimeSpan? MaxPenaltyDuration { get; set; }

    /// <summary>
    /// Custom parameters
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();
}

/// <summary>
/// Conditions for applying rate limit rules
/// </summary>
public class RateLimitConditions
{
    /// <summary>
    /// Specific client IDs to match
    /// </summary>
    public List<string> ClientIds { get; set; } = new();

    /// <summary>
    /// User roles to match
    /// </summary>
    public List<string> UserRoles { get; set; } = new();

    /// <summary>
    /// Endpoints to match (supports wildcards)
    /// </summary>
    public List<string> Endpoints { get; set; } = new();

    /// <summary>
    /// HTTP methods to match
    /// </summary>
    public List<string> Methods { get; set; } = new();

    /// <summary>
    /// IP address patterns to match
    /// </summary>
    public List<string> IpPatterns { get; set; } = new();

    /// <summary>
    /// User agent patterns to match
    /// </summary>
    public List<string> UserAgentPatterns { get; set; } = new();

    /// <summary>
    /// Time-based conditions
    /// </summary>
    public TimeConditions? TimeConditions { get; set; }

    /// <summary>
    /// Request size conditions
    /// </summary>
    public SizeConditions? SizeConditions { get; set; }

    /// <summary>
    /// Custom condition function
    /// </summary>
    public Func<RateLimitRequest, bool>? CustomCondition { get; set; }
}

/// <summary>
/// Time-based conditions
/// </summary>
public class TimeConditions
{
    /// <summary>
    /// Days of week when rule applies
    /// </summary>
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    /// <summary>
    /// Hours of day when rule applies (0-23)
    /// </summary>
    public List<int> HoursOfDay { get; set; } = new();

    /// <summary>
    /// Date range when rule applies
    /// </summary>
    public DateTimeOffset? StartDate { get; set; }

    /// <summary>
    /// Date range when rule applies
    /// </summary>
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// Timezone for time-based conditions
    /// </summary>
    public string? TimeZone { get; set; }
}

/// <summary>
/// Size-based conditions
/// </summary>
public class SizeConditions
{
    /// <summary>
    /// Minimum request size in bytes
    /// </summary>
    public long? MinSize { get; set; }

    /// <summary>
    /// Maximum request size in bytes
    /// </summary>
    public long? MaxSize { get; set; }
}

/// <summary>
/// Actions to take when rate limit is exceeded
/// </summary>
public class RateLimitActions
{
    /// <summary>
    /// Block the request
    /// </summary>
    public bool Block { get; set; } = true;

    /// <summary>
    /// Log the violation
    /// </summary>
    public bool Log { get; set; } = true;

    /// <summary>
    /// Send notification
    /// </summary>
    public bool Notify { get; set; } = false;

    /// <summary>
    /// Custom response status code
    /// </summary>
    public int? CustomStatusCode { get; set; }

    /// <summary>
    /// Custom response message
    /// </summary>
    public string? CustomMessage { get; set; }

    /// <summary>
    /// Additional response headers
    /// </summary>
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();

    /// <summary>
    /// Delay before responding (throttling)
    /// </summary>
    public TimeSpan? ResponseDelay { get; set; }

    /// <summary>
    /// Custom action callback
    /// </summary>
    public Func<RateLimitRequest, RateLimitResult, Task>? CustomAction { get; set; }
}

/// <summary>
/// Rate limiting algorithms
/// </summary>
public enum RateLimitAlgorithm
{
    /// <summary>
    /// Fixed window counter
    /// </summary>
    FixedWindow,

    /// <summary>
    /// Sliding window counter
    /// </summary>
    SlidingWindow,

    /// <summary>
    /// Token bucket algorithm
    /// </summary>
    TokenBucket,

    /// <summary>
    /// Leaky bucket algorithm
    /// </summary>
    LeakyBucket,

    /// <summary>
    /// Sliding window log
    /// </summary>
    SlidingWindowLog
}

/// <summary>
/// Rate limit violation severity
/// </summary>
public enum RateLimitSeverity
{
    Normal,
    Warning,
    Severe,
    Critical
}

/// <summary>
/// Current rate limit status for a client
/// </summary>
public class RateLimitStatus
{
    /// <summary>
    /// Client identifier
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Whether client is currently rate limited
    /// </summary>
    public bool IsRateLimited { get; set; }

    /// <summary>
    /// Whether client is whitelisted
    /// </summary>
    public bool IsWhitelisted { get; set; }

    /// <summary>
    /// Whether client is blacklisted
    /// </summary>
    public bool IsBlacklisted { get; set; }

    /// <summary>
    /// Active rate limit windows
    /// </summary>
    public List<RateLimitWindow> ActiveWindows { get; set; } = new();

    /// <summary>
    /// Recent violations
    /// </summary>
    public List<RateLimitViolation> RecentViolations { get; set; } = new();

    /// <summary>
    /// Current penalty level
    /// </summary>
    public double PenaltyLevel { get; set; }

    /// <summary>
    /// When penalties will be reset
    /// </summary>
    public DateTime? PenaltyResetTime { get; set; }
}

/// <summary>
/// Rate limit window information
/// </summary>
public class RateLimitWindow
{
    /// <summary>
    /// Rule that created this window
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Window start time
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Window end time
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Current request count
    /// </summary>
    public long RequestCount { get; set; }

    /// <summary>
    /// Request limit for this window
    /// </summary>
    public long Limit { get; set; }

    /// <summary>
    /// Whether this window has been exceeded
    /// </summary>
    public bool IsExceeded { get; set; }
}

/// <summary>
/// Rate limit violation record
/// </summary>
public class RateLimitViolation
{
    /// <summary>
    /// When the violation occurred
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Rule that was violated
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    /// Endpoint that was accessed
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Request count when violation occurred
    /// </summary>
    public long RequestCount { get; set; }

    /// <summary>
    /// Limit that was exceeded
    /// </summary>
    public long Limit { get; set; }

    /// <summary>
    /// Violation severity
    /// </summary>
    public RateLimitSeverity Severity { get; set; }
}

/// <summary>
/// Rate limiting statistics
/// </summary>
public class RateLimitStatistics
{
    /// <summary>
    /// Total requests processed
    /// </summary>
    public long TotalRequests { get; set; }

    /// <summary>
    /// Total requests blocked
    /// </summary>
    public long BlockedRequests { get; set; }

    /// <summary>
    /// Total violations
    /// </summary>
    public long TotalViolations { get; set; }

    /// <summary>
    /// Unique clients rate limited
    /// </summary>
    public long UniqueClientsLimited { get; set; }

    /// <summary>
    /// Top violating clients
    /// </summary>
    public Dictionary<string, long> TopViolatingClients { get; set; } = new();

    /// <summary>
    /// Top targeted endpoints
    /// </summary>
    public Dictionary<string, long> TopTargetedEndpoints { get; set; } = new();

    /// <summary>
    /// Violations by rule
    /// </summary>
    public Dictionary<string, long> ViolationsByRule { get; set; } = new();

    /// <summary>
    /// Violations by hour
    /// </summary>
    public Dictionary<DateTime, long> ViolationsByHour { get; set; } = new();

    /// <summary>
    /// Average response time impact
    /// </summary>
    public double AverageResponseTimeImpact { get; set; }

    /// <summary>
    /// Statistics generation time
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}