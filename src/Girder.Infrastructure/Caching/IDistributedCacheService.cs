namespace Infrastructure.Caching;

/// <summary>
/// Enhanced distributed cache service with advanced features
/// </summary>
public interface IDistributedCacheService
{
    /// <summary>
    /// Get value from cache
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Get value from cache with fallback to source
    /// </summary>
    Task<T> GetOrSetAsync<T>(
        string key, 
        Func<Task<T>> factory, 
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Set value in cache
    /// </summary>
    Task SetAsync<T>(
        string key, 
        T value, 
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Remove value from cache
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove multiple keys from cache
    /// </summary>
    Task RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cache entries by pattern
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cache entries by tags
    /// </summary>
    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove cache entries by multiple tags
    /// </summary>
    Task RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if key exists in cache
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh cache entry (extend TTL)
    /// </summary>
    Task RefreshAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get multiple values from cache
    /// </summary>
    Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Set multiple values in cache
    /// </summary>
    Task SetManyAsync<T>(
        Dictionary<string, T> keyValues, 
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Warm cache with multiple values
    /// </summary>
    Task WarmCacheAsync<T>(
        Dictionary<string, Func<Task<T>>> keyFactories,
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class;
}

/// <summary>
/// Cache configuration options
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Tags for cache invalidation
    /// </summary>
    public HashSet<string> Tags { get; set; } = new();

    /// <summary>
    /// Priority for cache eviction
    /// </summary>
    public CachePriority Priority { get; set; } = CachePriority.Normal;

    /// <summary>
    /// Whether to compress the cached value
    /// </summary>
    public bool Compress { get; set; } = false;

    /// <summary>
    /// Whether to encrypt the cached value
    /// </summary>
    public bool Encrypt { get; set; } = false;

    /// <summary>
    /// Sliding expiration (extends TTL on access)
    /// </summary>
    public TimeSpan? SlidingExpiration { get; set; }

    /// <summary>
    /// Absolute expiration (fixed expiry time)
    /// </summary>
    public DateTimeOffset? AbsoluteExpiration { get; set; }

    /// <summary>
    /// Region for logical grouping
    /// </summary>
    public string? Region { get; set; }
}

/// <summary>
/// Cache priority for eviction policies
/// </summary>
public enum CachePriority
{
    Low,
    Normal,
    High,
    Critical
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// Total number of cache hits
    /// </summary>
    public long Hits { get; set; }

    /// <summary>
    /// Total number of cache misses
    /// </summary>
    public long Misses { get; set; }

    /// <summary>
    /// Cache hit ratio
    /// </summary>
    public double HitRatio => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0;

    /// <summary>
    /// Number of keys in cache
    /// </summary>
    public long KeyCount { get; set; }

    /// <summary>
    /// Memory usage in bytes
    /// </summary>
    public long MemoryUsage { get; set; }

    /// <summary>
    /// Number of evictions
    /// </summary>
    public long Evictions { get; set; }

    /// <summary>
    /// Number of expirations
    /// </summary>
    public long Expirations { get; set; }

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// Cache instance information
    /// </summary>
    public string? InstanceInfo { get; set; }

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}