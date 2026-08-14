namespace Infrastructure.Communication.Configuration;

/// <summary>
/// Cache configuration for service responses
/// </summary>
public class CacheConfiguration
{
    /// <summary>
    /// Default time-to-live for cached responses
    /// </summary>
    public TimeSpan DefaultTTL { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of cached responses
    /// </summary>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// Enable ETag support for conditional requests
    /// </summary>
    public bool EnableETagSupport { get; set; } = true;

    /// <summary>
    /// Respect Cache-Control headers from responses
    /// </summary>
    public bool RespectCacheControlHeaders { get; set; } = true;

    /// <summary>
    /// Per-service cache policies
    /// </summary>
    public Dictionary<string, ServiceCachePolicy> PerServicePolicies { get; set; } = new();

    /// <summary>
    /// Cache key prefix
    /// </summary>
    public string CacheKeyPrefix { get; set; } = "svc:";

    /// <summary>
    /// Enable cache compression
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Minimum size in bytes for compression
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024; // 1KB
}

/// <summary>
/// Cache policy for a specific service
/// </summary>
public class ServiceCachePolicy
{
    /// <summary>
    /// Whether caching is enabled for this service
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Time-to-live for cached responses from this service
    /// </summary>
    public TimeSpan TTL { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Cache only successful responses (2xx)
    /// </summary>
    public bool CacheOnlySuccess { get; set; } = true;

    /// <summary>
    /// HTTP methods to cache
    /// </summary>
    public HashSet<string> CacheableMethods { get; set; } = new() { "GET", "HEAD" };

    /// <summary>
    /// Endpoints to exclude from caching (regex patterns)
    /// </summary>
    public HashSet<string> ExcludePatterns { get; set; } = new();

    /// <summary>
    /// Endpoints to always cache (regex patterns)
    /// </summary>
    public HashSet<string> IncludePatterns { get; set; } = new();
}
