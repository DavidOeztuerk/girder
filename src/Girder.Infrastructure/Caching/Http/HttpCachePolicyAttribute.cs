using System;

namespace Infrastructure.Caching.Http;

/// <summary>
/// Attribute to configure HTTP caching behavior at the controller or action level.
/// </summary>
/// <example>
/// <code>
/// [HttpCachePolicy(MaxAge = 300, Private = true, VaryByUser = true)]
/// [HttpGet("{id}")]
/// public async Task&lt;ActionResult&lt;UserProfile&gt;&gt; GetProfile(Guid id) { ... }
///
/// [HttpCachePolicy(NoStore = true)]
/// [HttpGet("current-status")]
/// public async Task&lt;ActionResult&lt;Status&gt;&gt; GetStatus() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class HttpCachePolicyAttribute : Attribute
{
    /// <summary>
    /// Maximum age in seconds for the cached response.
    /// </summary>
    public int MaxAge { get; set; }

    /// <summary>
    /// Whether the response is private (user-specific).
    /// Default is true for security.
    /// </summary>
    public bool Private { get; set; } = true;

    /// <summary>
    /// Whether to completely disable caching (adds no-store directive).
    /// </summary>
    public bool NoStore { get; set; }

    /// <summary>
    /// Whether to require revalidation on each request (adds no-cache directive).
    /// </summary>
    public bool NoCache { get; set; }

    /// <summary>
    /// Whether to add must-revalidate directive.
    /// </summary>
    public bool MustRevalidate { get; set; }

    /// <summary>
    /// Whether response varies by user (adds Authorization to Vary header).
    /// </summary>
    public bool VaryByUser { get; set; }

    /// <summary>
    /// Custom headers to add to the Vary header (comma-separated).
    /// </summary>
    public string? VaryByHeaders { get; set; }

    /// <summary>
    /// Gets the Vary headers as an array.
    /// </summary>
    public string[] GetVaryHeaders()
    {
        var headers = new System.Collections.Generic.List<string>();

        if (VaryByUser)
        {
            headers.Add("Authorization");
        }

        if (!string.IsNullOrWhiteSpace(VaryByHeaders))
        {
            headers.AddRange(VaryByHeaders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return headers.ToArray();
    }

    /// <summary>
    /// Builds the Cache-Control header value.
    /// </summary>
    public string BuildCacheControlHeader()
    {
        if (NoStore)
        {
            return "no-store";
        }

        var directives = new System.Collections.Generic.List<string>();

        if (NoCache)
        {
            directives.Add("no-cache");
        }

        directives.Add(Private ? "private" : "public");

        if (MaxAge > 0)
        {
            directives.Add($"max-age={MaxAge}");
        }

        if (MustRevalidate)
        {
            directives.Add("must-revalidate");
        }

        return string.Join(", ", directives);
    }
}
