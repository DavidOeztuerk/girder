namespace Infrastructure.Caching;

/// <summary>
/// Interface for distributed rate limiting storage
/// </summary>
public interface IDistributedRateLimitStore
{
    /// <summary>
    /// Get current count for a rate limit key
    /// </summary>
    Task<long> GetCountAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment count for a rate limit key with expiration
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a key exists
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set expiration for a key
    /// </summary>
    Task<bool> ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get time to live for a key
    /// </summary>
    Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute a Lua script for atomic operations
    /// </summary>
    Task<long> ExecuteScriptAsync(string script, string[] keys, object[] values, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a key
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomic increment with sliding window rate limiting
    /// </summary>
    Task<RateLimitResult> SlidingWindowIncrementAsync(
        string key, 
        int limit, 
        TimeSpan window, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of rate limit check
/// </summary>
public record RateLimitResult
{
    /// <summary>
    /// Whether the request is allowed
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Current count in the window
    /// </summary>
    public long CurrentCount { get; init; }

    /// <summary>
    /// Rate limit for the window
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Time remaining until window resets
    /// </summary>
    public TimeSpan? ResetTime { get; init; }

    /// <summary>
    /// Number of requests remaining in the current window
    /// </summary>
    public long RemainingRequests => Math.Max(0, Limit - CurrentCount);
}