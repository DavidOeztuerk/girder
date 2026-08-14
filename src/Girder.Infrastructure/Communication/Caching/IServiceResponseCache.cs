namespace Infrastructure.Communication.Caching;

/// <summary>
/// Cache for service responses
/// </summary>
public interface IServiceResponseCache
{
    /// <summary>
    /// Get cached response
    /// </summary>
    Task<CachedResponse<T>?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Set cached response
    /// </summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        string? etag = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Remove cached response
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cached responses by pattern
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists in cache
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    CacheStatistics GetStatistics();
}

/// <summary>
/// Cached response wrapper
/// </summary>
public class CachedResponse<T> where T : class
{
    /// <summary>
    /// Cached data
    /// </summary>
    public T Data { get; set; } = default!;

    /// <summary>
    /// ETag for conditional requests
    /// </summary>
    public string? ETag { get; set; }

    /// <summary>
    /// When the response was cached
    /// </summary>
    public DateTime CachedAt { get; set; }

    /// <summary>
    /// When the cache expires
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Whether the cached response is still valid
    /// </summary>
    public bool IsValid => !ExpiresAt.HasValue || DateTime.UtcNow < ExpiresAt.Value;
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public long TotalRequests { get; set; }
    public long CacheHits { get; set; }
    public long CacheMisses { get; set; }
    public long CacheEvictions { get; set; }
    public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests * 100 : 0;
    public DateTime LastReset { get; set; } = DateTime.UtcNow;
}
