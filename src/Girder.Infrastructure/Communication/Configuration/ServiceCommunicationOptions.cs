namespace Infrastructure.Communication.Configuration;

/// <summary>
/// Configuration options for service communication
/// </summary>
public class ServiceCommunicationOptions
{
    public const string SectionName = "ServiceCommunication";

    /// <summary>
    /// Whether to route all requests through the API Gateway
    /// </summary>
    public bool UseGateway { get; set; } = true;

    /// <summary>
    /// Default timeout for service requests
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Enable response caching
    /// </summary>
    public bool EnableResponseCaching { get; set; } = true;

    /// <summary>
    /// Enable metrics collection
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable request deduplication
    /// </summary>
    public bool EnableRequestDeduplication { get; set; } = true;

    /// <summary>
    /// Retry policy configuration
    /// </summary>
    public RetryConfiguration RetryPolicy { get; set; } = new();

    /// <summary>
    /// Circuit breaker configuration
    /// </summary>
    public CircuitBreakerConfiguration CircuitBreaker { get; set; } = new();

    /// <summary>
    /// Caching configuration
    /// </summary>
    public CacheConfiguration Caching { get; set; } = new();

    /// <summary>
    /// M2M authentication configuration
    /// </summary>
    public M2MConfiguration M2M { get; set; } = new();

    /// <summary>
    /// Bulkhead configuration
    /// </summary>
    public BulkheadConfiguration Bulkhead { get; set; } = new();

    /// <summary>
    /// Telemetry configuration
    /// </summary>
    public TelemetryConfiguration Telemetry { get; set; } = new();

    /// <summary>
    /// Service-specific endpoint overrides
    /// </summary>
    public Dictionary<string, string> ServiceEndpoints { get; set; } = new();

    /// <summary>
    /// Bearer token for service-to-service authentication (fallback)
    /// </summary>
    public string? BearerToken { get; set; }
}

/// <summary>
/// Circuit breaker configuration
/// </summary>
public class CircuitBreakerConfiguration
{
    /// <summary>
    /// Number of consecutive exceptions before breaking the circuit
    /// </summary>
    public int ExceptionsAllowedBeforeBreaking { get; set; } = 5;

    /// <summary>
    /// Duration to keep the circuit open before attempting to close it
    /// </summary>
    public TimeSpan DurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for individual operations
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Failure threshold (0.0 to 1.0) for tripping based on failure rate
    /// </summary>
    public double FailureThreshold { get; set; } = 0.5;

    /// <summary>
    /// Minimum number of requests before failure rate is considered
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;
}

/// <summary>
/// Bulkhead configuration
/// </summary>
public class BulkheadConfiguration
{
    /// <summary>
    /// Maximum parallel requests across all services
    /// </summary>
    public int MaxParallelRequests { get; set; } = 100;

    /// <summary>
    /// Maximum queued requests
    /// </summary>
    public int MaxQueuedRequests { get; set; } = 50;

    /// <summary>
    /// Per-service request limits
    /// </summary>
    public Dictionary<string, int> PerServiceLimits { get; set; } = new();
}

/// <summary>
/// Telemetry configuration
/// </summary>
public class TelemetryConfiguration
{
    /// <summary>
    /// Enable detailed metrics collection
    /// </summary>
    public bool EnableDetailedMetrics { get; set; } = true;

    /// <summary>
    /// Trace request/response body content (for debugging)
    /// </summary>
    public bool TraceBodyContent { get; set; } = false;

    /// <summary>
    /// Sampling rate for telemetry (0.0 to 1.0)
    /// </summary>
    public double SampleRate { get; set; } = 1.0;
}
