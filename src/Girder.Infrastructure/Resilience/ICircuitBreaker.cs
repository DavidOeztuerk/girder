namespace Infrastructure.Resilience;

/// <summary>
/// Circuit breaker interface for resilience patterns
/// </summary>
public interface ICircuitBreaker
{
    /// <summary>
    /// Execute operation with circuit breaker protection
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute operation with circuit breaker protection and fallback
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>> fallback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute void operation with circuit breaker protection
    /// </summary>
    Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute void operation with circuit breaker protection and fallback
    /// </summary>
    Task ExecuteAsync(Func<Task> operation, Func<Task> fallback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Current state of the circuit breaker
    /// </summary>
    CircuitBreakerState State { get; }

    /// <summary>
    /// Get circuit breaker statistics
    /// </summary>
    CircuitBreakerStatistics GetStatistics();

    /// <summary>
    /// Reset the circuit breaker to closed state
    /// </summary>
    void Reset();

    /// <summary>
    /// Manually open the circuit breaker
    /// </summary>
    void ForceOpen();
}

/// <summary>
/// Circuit breaker state
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// Circuit is closed, requests are allowed through
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is open, requests are rejected immediately
    /// </summary>
    Open,

    /// <summary>
    /// Circuit is half-open, testing if service has recovered
    /// </summary>
    HalfOpen
}

/// <summary>
/// Circuit breaker statistics
/// </summary>
public class CircuitBreakerStatistics
{
    /// <summary>
    /// Current state of the circuit breaker
    /// </summary>
    public CircuitBreakerState State { get; set; }

    /// <summary>
    /// Total number of successful executions
    /// </summary>
    public long SuccessCount { get; set; }

    /// <summary>
    /// Total number of failed executions
    /// </summary>
    public long FailureCount { get; set; }

    /// <summary>
    /// Total number of executions
    /// </summary>
    public long TotalCount => SuccessCount + FailureCount;

    /// <summary>
    /// Success rate percentage
    /// </summary>
    public double SuccessRate => TotalCount > 0 ? (double)SuccessCount / TotalCount * 100 : 0;

    /// <summary>
    /// Failure rate percentage
    /// </summary>
    public double FailureRate => TotalCount > 0 ? (double)FailureCount / TotalCount * 100 : 0;

    /// <summary>
    /// Number of circuit breaker trips
    /// </summary>
    public long TripCount { get; set; }

    /// <summary>
    /// When the circuit was last opened
    /// </summary>
    public DateTime? LastOpenedAt { get; set; }

    /// <summary>
    /// When the circuit was last closed
    /// </summary>
    public DateTime? LastClosedAt { get; set; }

    /// <summary>
    /// Current consecutive failure count
    /// </summary>
    public int ConsecutiveFailures { get; set; }

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// Last error message
    /// </summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Statistics collection timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}