namespace Infrastructure.Resilience;

/// <summary>
/// Interface for retry policies
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Execute operation with retry policy
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute void operation with retry policy
    /// </summary>
    Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute operation with retry policy and custom retry predicate
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation, 
        Func<Exception, bool> shouldRetry, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute operation with retry policy and callback for each retry attempt
    /// </summary>
    Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        Action<int, Exception, TimeSpan> onRetry,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get retry policy statistics
    /// </summary>
    RetryPolicyStatistics GetStatistics();

    /// <summary>
    /// Reset retry policy statistics
    /// </summary>
    void ResetStatistics();
}

/// <summary>
/// Retry policy statistics
/// </summary>
public class RetryPolicyStatistics
{
    /// <summary>
    /// Total number of executions
    /// </summary>
    public long TotalExecutions { get; set; }

    /// <summary>
    /// Total number of successful executions (including after retries)
    /// </summary>
    public long SuccessfulExecutions { get; set; }

    /// <summary>
    /// Total number of failed executions (after all retries exhausted)
    /// </summary>
    public long FailedExecutions { get; set; }

    /// <summary>
    /// Total number of retry attempts
    /// </summary>
    public long RetryAttempts { get; set; }

    /// <summary>
    /// Success rate percentage
    /// </summary>
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions * 100 : 0;

    /// <summary>
    /// Average retry attempts per execution
    /// </summary>
    public double AverageRetryAttempts => TotalExecutions > 0 ? (double)RetryAttempts / TotalExecutions : 0;

    /// <summary>
    /// Distribution of retry counts
    /// </summary>
    public Dictionary<int, long> RetryDistribution { get; set; } = new();

    /// <summary>
    /// Most common exceptions that triggered retries
    /// </summary>
    public Dictionary<string, long> ExceptionTypes { get; set; } = new();

    /// <summary>
    /// Statistics collection timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Retry attempt information
/// </summary>
public class RetryAttempt
{
    /// <summary>
    /// Attempt number (0-based)
    /// </summary>
    public int AttemptNumber { get; set; }

    /// <summary>
    /// Exception that caused the retry
    /// </summary>
    public Exception Exception { get; set; } = null!;

    /// <summary>
    /// Delay before this attempt
    /// </summary>
    public TimeSpan Delay { get; set; }

    /// <summary>
    /// Timestamp of the attempt
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}