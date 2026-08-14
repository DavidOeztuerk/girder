namespace Infrastructure.Communication.Configuration;

/// <summary>
/// Retry policy configuration
/// </summary>
public class RetryConfiguration
{
    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Backoff strategy to use
    /// </summary>
    public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.ExponentialBackoff;

    /// <summary>
    /// Initial delay before first retry
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum delay between retries
    /// </summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// HTTP status codes that should trigger a retry
    /// </summary>
    public HashSet<int> RetryableStatusCodes { get; set; } = new()
    {
        408, // Request Timeout
        429, // Too Many Requests
        500, // Internal Server Error
        502, // Bad Gateway
        503, // Service Unavailable
        504  // Gateway Timeout
    };

    /// <summary>
    /// Exception types that should trigger a retry
    /// </summary>
    public HashSet<string> RetryableExceptions { get; set; } = new()
    {
        "TimeoutException",
        "HttpRequestException",
        "TaskCanceledException",
        "OperationCanceledException"
    };

    /// <summary>
    /// Whether to retry on timeout
    /// </summary>
    public bool RetryOnTimeout { get; set; } = true;

    /// <summary>
    /// Whether to add jitter to retry delays
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// Jitter factor (0.0 to 1.0)
    /// </summary>
    public double JitterFactor { get; set; } = 0.2;
}

/// <summary>
/// Backoff strategies for retry delays
/// </summary>
public enum BackoffStrategy
{
    /// <summary>
    /// Linear backoff (constant delay)
    /// </summary>
    Linear,

    /// <summary>
    /// Exponential backoff (delay doubles each retry)
    /// </summary>
    ExponentialBackoff,

    /// <summary>
    /// Exponential backoff with jitter
    /// </summary>
    ExponentialBackoffWithJitter,

    /// <summary>
    /// Fibonacci backoff
    /// </summary>
    Fibonacci,

    /// <summary>
    /// Custom backoff (requires custom implementation)
    /// </summary>
    Custom
}
