using System.Collections.Generic;

namespace Infrastructure.Caching.Http;

/// <summary>
/// Configuration options for HTTP response caching.
/// </summary>
public class HttpCachingOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "HttpCaching";

    /// <summary>
    /// Whether HTTP response caching is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default max-age for private responses in seconds.
    /// </summary>
    public int DefaultPrivateMaxAge { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Default max-age for public responses in seconds.
    /// </summary>
    public int DefaultPublicMaxAge { get; set; } = 3600; // 1 hour

    /// <summary>
    /// Whether ETag generation is enabled.
    /// </summary>
    public bool ETagEnabled { get; set; } = true;

    /// <summary>
    /// Whether to add X-Cache-Status header (useful for debugging).
    /// </summary>
    public bool AddCacheStatusHeader { get; set; } = true;

    /// <summary>
    /// Cache policies for specific endpoint patterns.
    /// </summary>
    public List<HttpCachePolicy> Policies { get; set; } = new();
}

/// <summary>
/// Cache policy configuration for a specific endpoint pattern.
/// </summary>
public class HttpCachePolicy
{
    /// <summary>
    /// The URL path pattern to match (supports wildcards: * and **).
    /// Examples: "/api/users/profile/*", "/api/skills/**"
    /// </summary>
    public string PathPattern { get; set; } = string.Empty;

    /// <summary>
    /// Maximum age in seconds. Set to 0 to disable caching.
    /// </summary>
    public int MaxAge { get; set; }

    /// <summary>
    /// Whether the response is private (user-specific) or public.
    /// </summary>
    public bool Private { get; set; } = true;

    /// <summary>
    /// Whether to add no-store directive (completely disable caching).
    /// </summary>
    public bool NoStore { get; set; }

    /// <summary>
    /// Whether to add no-cache directive (always revalidate).
    /// </summary>
    public bool NoCache { get; set; }

    /// <summary>
    /// Whether to add must-revalidate directive.
    /// </summary>
    public bool MustRevalidate { get; set; }

    /// <summary>
    /// Headers to include in the Vary header.
    /// </summary>
    public List<string> VaryByHeaders { get; set; } = new();

    /// <summary>
    /// Whether response varies by user (adds Authorization to Vary header).
    /// </summary>
    public bool VaryByUser { get; set; }
}
