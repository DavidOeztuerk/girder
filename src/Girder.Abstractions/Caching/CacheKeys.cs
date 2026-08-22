namespace Girder.Abstractions.Caching;

/// <summary>
/// Key prefixes two parts of Girder have to agree on.
/// </summary>
/// <remarks>
/// An agreement about a key exists whether or not it is written down. Naming it
/// here is what lets the HTTP middleware that stores an ETag and the command
/// pipeline that clears it meet on the same string without knowing about each
/// other.
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
