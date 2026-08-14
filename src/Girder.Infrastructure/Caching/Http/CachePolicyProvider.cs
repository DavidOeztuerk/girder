using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Caching.Http;

/// <summary>
/// Interface for providing cache policies based on request context.
/// </summary>
public interface ICachePolicyProvider
{
    /// <summary>
    /// Gets the cache policy for the given HTTP context.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The applicable cache policy, or null if caching should be disabled.</returns>
    CachePolicyResult? GetCachePolicy(HttpContext context);

    /// <summary>
    /// Gets the cache policy for the given path and method.
    /// </summary>
    /// <param name="path">The request path.</param>
    /// <param name="method">The HTTP method.</param>
    /// <returns>The applicable cache policy, or null if caching should be disabled.</returns>
    CachePolicyResult? GetCachePolicy(string path, string method);
}

/// <summary>
/// Result of cache policy evaluation.
/// </summary>
public class CachePolicyResult
{
    /// <summary>
    /// The Cache-Control header value.
    /// </summary>
    public string CacheControl { get; set; } = string.Empty;

    /// <summary>
    /// Maximum age in seconds.
    /// </summary>
    public int MaxAge { get; set; }

    /// <summary>
    /// Whether caching is completely disabled (no-store).
    /// </summary>
    public bool NoStore { get; set; }

    /// <summary>
    /// Whether revalidation is required (no-cache).
    /// </summary>
    public bool NoCache { get; set; }

    /// <summary>
    /// Whether the response is private.
    /// </summary>
    public bool IsPrivate { get; set; }

    /// <summary>
    /// Headers to include in the Vary header.
    /// </summary>
    public List<string> VaryHeaders { get; set; } = new();

    /// <summary>
    /// Whether ETags should be generated.
    /// </summary>
    public bool GenerateETag { get; set; } = true;

    /// <summary>
    /// Source of the policy (for debugging).
    /// </summary>
    public string PolicySource { get; set; } = "default";
}

/// <summary>
/// Default implementation of cache policy provider.
/// </summary>
public class CachePolicyProvider : ICachePolicyProvider
{
    private readonly HttpCachingOptions _options;
    private readonly ILogger<CachePolicyProvider> _logger;
    private readonly List<CompiledCachePolicy> _compiledPolicies;

    // HTTP methods that should never be cached
    private static readonly HashSet<string> NonCacheableMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE", "OPTIONS"
    };

    // Paths that should never be cached
    // IMPORTANT: VideoCall/WebRTC endpoints must NEVER be cached due to:
    // - Real-time nature (session status, participants change constantly)
    // - WebRTC signaling (ICE candidates, offers/answers must be fresh)
    // - Security (session tokens, encryption keys)
    // - Consistency (stale data = failed calls)
    private static readonly string[] NonCacheablePaths =
    {
        "/hub/",                    // SignalR hubs
        "/hubs/",                   // SignalR hubs (alternate path)
        "/api/auth/",               // Authentication endpoints
        "/api/videocall/",          // VideoCall REST API
        "/api/calls/",              // VideoCall sessions
        "/api/my/calls",            // User's calls
        "/health",                  // Health checks
        "/swagger",                 // Swagger UI
        "/hangfire"                 // Hangfire dashboard
    };

    /// <summary>
    /// Initializes a new instance of the CachePolicyProvider.
    /// </summary>
    public CachePolicyProvider(
        IOptions<HttpCachingOptions> options,
        ILogger<CachePolicyProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
        _compiledPolicies = CompilePolicies(_options.Policies);
    }

    /// <inheritdoc />
    public CachePolicyResult? GetCachePolicy(HttpContext context)
    {
        return GetCachePolicy(context.Request.Path.Value ?? "/", context.Request.Method);
    }

    /// <inheritdoc />
    public CachePolicyResult? GetCachePolicy(string path, string method)
    {
        // Check if HTTP caching is enabled
        if (!_options.Enabled)
        {
            _logger.LogDebug("HTTP caching is disabled globally");
            return null;
        }

        // Non-cacheable methods
        if (NonCacheableMethods.Contains(method))
        {
            return CreateNoStorePolicy("non-cacheable-method");
        }

        // Non-cacheable paths
        if (IsNonCacheablePath(path))
        {
            return CreateNoStorePolicy("non-cacheable-path");
        }

        // Find matching policy from configuration
        var matchedPolicy = FindMatchingPolicy(path);
        if (matchedPolicy != null)
        {
            return CreatePolicyResult(matchedPolicy, "config");
        }

        // Return default policy based on path characteristics
        return CreateDefaultPolicy(path);
    }

    private bool IsNonCacheablePath(string path)
    {
        return NonCacheablePaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private CompiledCachePolicy? FindMatchingPolicy(string path)
    {
        return _compiledPolicies.FirstOrDefault(p => p.Regex.IsMatch(path));
    }

    private CachePolicyResult CreateNoStorePolicy(string source)
    {
        return new CachePolicyResult
        {
            CacheControl = "no-store",
            NoStore = true,
            GenerateETag = false,
            PolicySource = source
        };
    }

    private CachePolicyResult CreatePolicyResult(CompiledCachePolicy policy, string source)
    {
        var result = new CachePolicyResult
        {
            MaxAge = policy.Policy.MaxAge,
            NoStore = policy.Policy.NoStore,
            NoCache = policy.Policy.NoCache,
            IsPrivate = policy.Policy.Private,
            GenerateETag = _options.ETagEnabled && !policy.Policy.NoStore,
            PolicySource = $"{source}:{policy.Policy.PathPattern}"
        };

        // Build Vary headers
        if (policy.Policy.VaryByUser || policy.Policy.VaryByHeaders.Contains("Authorization"))
        {
            result.VaryHeaders.Add("Authorization");
        }
        result.VaryHeaders.AddRange(policy.Policy.VaryByHeaders.Where(h => h != "Authorization"));

        // Build Cache-Control header
        result.CacheControl = BuildCacheControlHeader(result);

        return result;
    }

    private CachePolicyResult CreateDefaultPolicy(string path)
    {
        // Determine if this is likely user-specific data
        var isUserSpecific = path.Contains("/profile", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/user", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/my-", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/my/", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/skills", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/appointments", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/matches", StringComparison.OrdinalIgnoreCase) ||
                            path.Contains("/notifications", StringComparison.OrdinalIgnoreCase);

        // CRITICAL FIX: For user-specific data, use no-store to completely disable caching
        // This is the ONLY way to guarantee the browser ALWAYS fetches fresh data
        // ETag-based caching (no-cache + revalidation) was not reliable enough
        if (isUserSpecific)
        {
            return new CachePolicyResult
            {
                CacheControl = "no-store, no-cache, must-revalidate, proxy-revalidate",
                NoStore = true,
                NoCache = true,
                IsPrivate = true,
                GenerateETag = false, // No ETags for no-store responses
                VaryHeaders = new List<string> { "Authorization" },
                PolicySource = "default-private"
            };
        }

        // Public data (categories, proficiency levels, etc.) can be cached
        var result = new CachePolicyResult
        {
            IsPrivate = false,
            MaxAge = _options.DefaultPublicMaxAge,
            NoCache = false,
            GenerateETag = _options.ETagEnabled,
            PolicySource = "default-public"
        };

        result.CacheControl = BuildCacheControlHeader(result);

        return result;
    }

    private static string BuildCacheControlHeader(CachePolicyResult policy)
    {
        // For no-store, return the most aggressive cache-busting header
        if (policy.NoStore)
        {
            return "no-store, no-cache, must-revalidate, proxy-revalidate, max-age=0";
        }

        var directives = new List<string>();

        if (policy.NoCache)
        {
            directives.Add("no-cache");
            directives.Add("must-revalidate");
        }

        directives.Add(policy.IsPrivate ? "private" : "public");

        if (policy.MaxAge >= 0)
        {
            directives.Add($"max-age={policy.MaxAge}");
        }

        return string.Join(", ", directives);
    }

    private static List<CompiledCachePolicy> CompilePolicies(List<HttpCachePolicy> policies)
    {
        var compiled = new List<CompiledCachePolicy>();

        foreach (var policy in policies)
        {
            try
            {
                // Convert glob pattern to regex
                var pattern = ConvertGlobToRegex(policy.PathPattern);
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

                compiled.Add(new CompiledCachePolicy
                {
                    Policy = policy,
                    Regex = regex
                });
            }
            catch (Exception ex)
            {
                // Log and skip invalid patterns
                Console.WriteLine($"Invalid cache policy pattern '{policy.PathPattern}': {ex.Message}");
            }
        }

        return compiled;
    }

    private static string ConvertGlobToRegex(string glob)
    {
        // Escape regex special characters except our wildcards
        var escaped = Regex.Escape(glob);

        // Convert ** (match any path segments) to regex
        escaped = escaped.Replace(@"\*\*", ".*");

        // Convert * (match single path segment) to regex
        escaped = escaped.Replace(@"\*", "[^/]*");

        // Convert {param} style parameters to regex
        escaped = Regex.Replace(escaped, @"\\\{[^}]+\\\}", "[^/]+");

        return $"^{escaped}$";
    }

    private class CompiledCachePolicy
    {
        public HttpCachePolicy Policy { get; set; } = null!;
        public Regex Regex { get; set; } = null!;
    }
}
