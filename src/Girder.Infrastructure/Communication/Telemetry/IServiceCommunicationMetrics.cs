namespace Infrastructure.Communication.Telemetry;

/// <summary>
/// Metrics tracking for service communication
/// </summary>
public interface IServiceCommunicationMetrics
{
    /// <summary>
    /// Record a service call
    /// </summary>
    void RecordServiceCall(string serviceName, string endpoint, string method, int statusCode, TimeSpan duration, bool fromCache = false);

    /// <summary>
    /// Record a service call failure
    /// </summary>
    void RecordServiceCallFailure(string serviceName, string endpoint, string method, string errorType, TimeSpan duration);

    /// <summary>
    /// Record a retry attempt
    /// </summary>
    void RecordRetryAttempt(string serviceName, int attemptNumber);

    /// <summary>
    /// Record circuit breaker state change
    /// </summary>
    void RecordCircuitBreakerStateChange(string serviceName, string state);

    /// <summary>
    /// Record cache operation
    /// </summary>
    void RecordCacheOperation(string serviceName, string operation, bool success);

    /// <summary>
    /// Record M2M token operation
    /// </summary>
    void RecordTokenOperation(string operation, bool success, TimeSpan? duration = null);

    /// <summary>
    /// Get current metrics summary
    /// </summary>
    ServiceCommunicationMetricsSummary GetMetricsSummary();

    /// <summary>
    /// Get metrics for a specific service
    /// </summary>
    ServiceMetrics? GetServiceMetrics(string serviceName);

    /// <summary>
    /// Reset all metrics
    /// </summary>
    void ResetMetrics();
}

/// <summary>
/// Summary of all service communication metrics
/// </summary>
public class ServiceCommunicationMetricsSummary
{
    public DateTime CollectionStartedAt { get; set; } = DateTime.UtcNow;
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public long CachedRequests { get; set; }
    public long RetryAttempts { get; set; }
    public double AverageResponseTime { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests * 100 : 0;
    public double CacheHitRate => TotalRequests > 0 ? (double)CachedRequests / TotalRequests * 100 : 0;
    public Dictionary<string, ServiceMetrics> ServiceMetrics { get; set; } = new();
    public Dictionary<string, long> StatusCodeDistribution { get; set; } = new();
    public Dictionary<string, long> ErrorTypeDistribution { get; set; } = new();
}

/// <summary>
/// Metrics for a specific service
/// </summary>
public class ServiceMetrics
{
    public string ServiceName { get; set; } = string.Empty;
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public long CachedRequests { get; set; }
    public long RetryAttempts { get; set; }
    public double AverageResponseTime { get; set; }
    public double P50ResponseTime { get; set; }
    public double P95ResponseTime { get; set; }
    public double P99ResponseTime { get; set; }
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests * 100 : 0;
    public Dictionary<string, EndpointMetrics> EndpointMetrics { get; set; } = new();
    public string? CircuitBreakerState { get; set; }
    public DateTime? LastRequestAt { get; set; }
}

/// <summary>
/// Metrics for a specific endpoint
/// </summary>
public class EndpointMetrics
{
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public long CachedRequests { get; set; }
    public double AverageResponseTime { get; set; }
    public Dictionary<int, long> StatusCodeCounts { get; set; } = new();
}
