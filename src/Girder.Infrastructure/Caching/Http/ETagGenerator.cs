using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure.Caching;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Caching.Http;

/// <summary>
/// Interface for generating and validating ETags for HTTP responses.
/// </summary>
public interface IETagGenerator
{
    /// <summary>
    /// Generates an ETag from the response body content.
    /// </summary>
    /// <param name="content">The response body as bytes.</param>
    /// <returns>The generated ETag value (including quotes).</returns>
    string GenerateETag(byte[] content);

    /// <summary>
    /// Generates an ETag from the response body content.
    /// </summary>
    /// <param name="content">The response body as string.</param>
    /// <returns>The generated ETag value (including quotes).</returns>
    string GenerateETag(string content);

    /// <summary>
    /// Generates an ETag from entity metadata (faster than hashing full body).
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="updatedAt">The last modification timestamp.</param>
    /// <returns>The generated ETag value (including quotes).</returns>
    string GenerateETag(Guid entityId, DateTime updatedAt);

    /// <summary>
    /// Generates an ETag from entity metadata with additional version info.
    /// </summary>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="updatedAt">The last modification timestamp.</param>
    /// <param name="version">Optional version number.</param>
    /// <returns>The generated ETag value (including quotes).</returns>
    string GenerateETag(Guid entityId, DateTime updatedAt, int? version);

    /// <summary>
    /// Validates if the provided ETag matches the current content.
    /// </summary>
    /// <param name="providedETag">The ETag from If-None-Match header.</param>
    /// <param name="currentETag">The current ETag of the resource.</param>
    /// <returns>True if the ETags match (content unchanged).</returns>
    bool ValidateETag(string? providedETag, string currentETag);

    /// <summary>
    /// Stores an ETag in the cache for later validation.
    /// </summary>
    /// <param name="cacheKey">The cache key for this ETag.</param>
    /// <param name="etag">The ETag value.</param>
    /// <param name="expiry">How long to cache the ETag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreETagAsync(string cacheKey, string etag, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a cached ETag.
    /// </summary>
    /// <param name="cacheKey">The cache key for this ETag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cached ETag or null if not found.</returns>
    Task<string?> GetCachedETagAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates a cached ETag.
    /// </summary>
    /// <param name="cacheKey">The cache key for this ETag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateETagAsync(string cacheKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates cached ETags by pattern.
    /// </summary>
    /// <param name="pattern">The pattern to match (e.g., "/api/skills*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateETagsByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of ETag generator using SHA256 hashing.
/// </summary>
public class ETagGenerator : IETagGenerator
{
    private readonly IDistributedCacheService? _cacheService;
    private readonly ILogger<ETagGenerator> _logger;
    private static readonly TimeSpan DefaultETagExpiry = TimeSpan.FromMinutes(5);
    private const string ETagCachePrefix = "etag:";

    /// <summary>
    /// Initializes a new instance of the ETagGenerator.
    /// </summary>
    /// <param name="cacheService">Optional distributed cache service for storing ETags.</param>
    /// <param name="logger">Logger instance.</param>
    public ETagGenerator(
        IDistributedCacheService? cacheService,
        ILogger<ETagGenerator> logger)
    {
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string GenerateETag(byte[] content)
    {
        if (content == null || content.Length == 0)
        {
            return "\"0\"";
        }

        var hash = SHA256.HashData(content);
        var hashString = Convert.ToBase64String(hash)[..16]; // Use first 16 chars for shorter ETag
        return $"\"{hashString}\"";
    }

    /// <inheritdoc />
    public string GenerateETag(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return "\"0\"";
        }

        return GenerateETag(Encoding.UTF8.GetBytes(content));
    }

    /// <inheritdoc />
    public string GenerateETag(Guid entityId, DateTime updatedAt)
    {
        return GenerateETag(entityId, updatedAt, null);
    }

    /// <inheritdoc />
    public string GenerateETag(Guid entityId, DateTime updatedAt, int? version)
    {
        var input = version.HasValue
            ? $"{entityId}-{updatedAt:O}-v{version}"
            : $"{entityId}-{updatedAt:O}";

        return GenerateETag(input);
    }

    /// <inheritdoc />
    public bool ValidateETag(string? providedETag, string currentETag)
    {
        if (string.IsNullOrWhiteSpace(providedETag))
        {
            return false;
        }

        // Handle weak ETags (W/"...")
        var normalizedProvided = NormalizeETag(providedETag);
        var normalizedCurrent = NormalizeETag(currentETag);

        // Check for wildcard
        if (normalizedProvided == "*")
        {
            return true;
        }

        // Support multiple ETags in If-None-Match (comma-separated)
        var providedETags = normalizedProvided.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var etag in providedETags)
        {
            if (string.Equals(NormalizeETag(etag), normalizedCurrent, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task StoreETagAsync(string cacheKey, string etag, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
        {
            _logger.LogDebug("ETag caching skipped - no cache service configured");
            return;
        }

        try
        {
            var fullKey = $"{ETagCachePrefix}{cacheKey}";
            await _cacheService.SetAsync(fullKey, etag, expiry ?? DefaultETagExpiry, null, cancellationToken);
            _logger.LogDebug("Stored ETag {ETag} for key {CacheKey}", etag, cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store ETag for key {CacheKey}", cacheKey);
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetCachedETagAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
        {
            return null;
        }

        try
        {
            var fullKey = $"{ETagCachePrefix}{cacheKey}";
            return await _cacheService.GetAsync<string>(fullKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve cached ETag for key {CacheKey}", cacheKey);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task InvalidateETagAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
        {
            return;
        }

        try
        {
            var fullKey = $"{ETagCachePrefix}{cacheKey}";
            await _cacheService.RemoveAsync(fullKey, cancellationToken);
            _logger.LogDebug("Invalidated ETag for key {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate ETag for key {CacheKey}", cacheKey);
        }
    }

    /// <inheritdoc />
    public async Task InvalidateETagsByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        if (_cacheService == null)
        {
            _logger.LogDebug("ETag pattern invalidation skipped - no cache service configured");
            return;
        }

        try
        {
            // ETagCachePrefix is already part of the key when storing (see StoreETagAsync)
            // RemoveByPatternAsync will add "cache:" prefix, so we need "etag:" in the pattern
            // Final Redis pattern: cache:etag:/api/skills*
            var fullPattern = $"{ETagCachePrefix}{pattern}";
            await _cacheService.RemoveByPatternAsync(fullPattern, cancellationToken);
            _logger.LogInformation("Invalidated ETags matching pattern {Pattern} (full: {FullPattern})", pattern, fullPattern);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate ETags for pattern {Pattern}", pattern);
        }
    }

    /// <summary>
    /// Normalizes an ETag by removing weak indicator and quotes.
    /// </summary>
    private static string NormalizeETag(string etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            return string.Empty;
        }

        // Remove weak indicator
        var normalized = etag.Trim();
        if (normalized.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        // Remove surrounding quotes
        normalized = normalized.Trim('"');

        return normalized;
    }
}
