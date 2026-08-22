namespace Girder.Infrastructure.Caching.Http;


/// <summary>
/// Interface for generating and validating ETags for HTTP responses.
/// </summary>
/// <remarks>
/// HTTP, and only HTTP: the patterns are API paths and the only thing that
/// registers it is <c>AddHttpResponseCaching()</c>. It lives beside its
/// implementation and its consumer so that no transport-independent layer can
/// reach it — a service that serves no HTTP must never be made to register
/// response caching to start.
/// </remarks>
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
    /// <param name="pattern">The pattern to match (e.g., "/api/jobs*").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InvalidateETagsByPatternAsync(string pattern, CancellationToken cancellationToken = default);
}
