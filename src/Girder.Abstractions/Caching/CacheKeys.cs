namespace Girder.Abstractions.Caching;

/// <summary>
/// Key prefixes two parts of Girder have to agree on.
/// </summary>
/// <remarks>
/// An agreement about a key exists whether or not it is written down. This one
/// used to live as a <c>private const</c> in the HTTP ETag generator and as a
/// comment beside the code that had to match it — which is the same agreement,
/// just unnamed and unenforced.
/// </remarks>
public static class CacheKeys
{
    /// <summary>
    /// Where ETags live in the cache. The rest of the key is the API path the
    /// ETag was issued for, so <c>etag:/api/jobs*</c> reaches every ETag under
    /// that route.
    /// </summary>
    public const string ETagPrefix = "etag:";
}
