namespace Infrastructure.Resilience.Bulkhead;

/// <summary>
/// Bulkhead isolation policy for limiting concurrent requests
/// </summary>
public interface IBulkheadPolicy
{
    /// <summary>
    /// Execute operation with bulkhead protection
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute operation with bulkhead protection and fallback
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>> fallback, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get current bulkhead statistics
    /// </summary>
    BulkheadStatistics GetStatistics();

    /// <summary>
    /// Reset bulkhead statistics
    /// </summary>
    void ResetStatistics();
}

/// <summary>
/// Bulkhead statistics
/// </summary>
public class BulkheadStatistics
{
    public int MaxParallelization { get; set; }
    public int MaxQueuedActions { get; set; }
    public int CurrentParallelization { get; set; }
    public int CurrentQueuedActions { get; set; }
    public long TotalExecutions { get; set; }
    public long RejectedExecutions { get; set; }
    public long QueuedExecutions { get; set; }
    public double RejectionRate => TotalExecutions > 0 ? (double)RejectedExecutions / TotalExecutions * 100 : 0;
    public DateTime LastReset { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Exception thrown when bulkhead is full
/// </summary>
public class BulkheadRejectedException : Exception
{
    public BulkheadRejectedException(string message) : base(message) { }
}
