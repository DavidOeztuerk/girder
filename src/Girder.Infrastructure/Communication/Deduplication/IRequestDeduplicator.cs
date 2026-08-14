namespace Infrastructure.Communication.Deduplication;

/// <summary>
/// Request deduplicator for preventing duplicate concurrent requests
/// </summary>
public interface IRequestDeduplicator
{
    /// <summary>
    /// Execute operation with deduplication (Single Flight pattern)
    /// </summary>
    Task<T?> ExecuteAsync<T>(string key, Func<Task<T?>> operation, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Get deduplication statistics
    /// </summary>
    DeduplicationStatistics GetStatistics();

    /// <summary>
    /// Clear all in-flight requests
    /// </summary>
    void ClearInflightRequests();
}

/// <summary>
/// Deduplication statistics
/// </summary>
public class DeduplicationStatistics
{
    public long TotalRequests { get; set; }
    public long DeduplicatedRequests { get; set; }
    public long UniqueRequests { get; set; }
    public int CurrentInflightRequests { get; set; }
    public double DeduplicationRate => TotalRequests > 0 ? (double)DeduplicatedRequests / TotalRequests * 100 : 0;
    public DateTime LastReset { get; set; } = DateTime.UtcNow;
}
