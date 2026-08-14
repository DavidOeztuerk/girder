using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Infrastructure.Caching.Http;

/// <summary>
/// Middleware that automatically adds HTTP caching headers (Cache-Control, ETag, Last-Modified)
/// to responses based on configured policies.
/// </summary>
/// <remarks>
/// This middleware should be placed after authentication and before the endpoint routing
/// in the request pipeline.
/// </remarks>
public class ResponseCachingHeaderMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICachePolicyProvider _policyProvider;
    private readonly IETagGenerator _etagGenerator;
    private readonly HttpCachingOptions _options;
    private readonly ILogger<ResponseCachingHeaderMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the ResponseCachingHeaderMiddleware.
    /// </summary>
    public ResponseCachingHeaderMiddleware(
        RequestDelegate next,
        ICachePolicyProvider policyProvider,
        IETagGenerator etagGenerator,
        IOptions<HttpCachingOptions> options,
        ILogger<ResponseCachingHeaderMiddleware> logger)
    {
        _next = next;
        _policyProvider = policyProvider;
        _etagGenerator = etagGenerator;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Processes the HTTP request and adds caching headers to the response.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip if caching is disabled
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // Get cache policy for this request
        var policy = GetEffectiveCachePolicy(context);

        // If no caching, just pass through
        if (policy == null || policy.NoStore)
        {
            if (policy?.NoStore == true)
            {
                context.Response.Headers[HeaderNames.CacheControl] = "no-store";
                AddCacheStatusHeader(context, "BYPASS");
            }

            await _next(context);
            return;
        }

        // Check for conditional request (If-None-Match)
        var ifNoneMatch = context.Request.Headers[HeaderNames.IfNoneMatch].FirstOrDefault();
        var ifModifiedSince = context.Request.Headers[HeaderNames.IfModifiedSince].FirstOrDefault();

        // Try to get cached ETag for this request
        var cacheKey = GenerateCacheKey(context);
        var cachedETag = await _etagGenerator.GetCachedETagAsync(cacheKey);

        // Check if we can return 304 Not Modified
        if (!string.IsNullOrEmpty(ifNoneMatch) && !string.IsNullOrEmpty(cachedETag))
        {
            if (_etagGenerator.ValidateETag(ifNoneMatch, cachedETag))
            {
                _logger.LogDebug("Returning 304 Not Modified for {Path}", context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.Headers[HeaderNames.ETag] = cachedETag;
                context.Response.Headers[HeaderNames.CacheControl] = policy.CacheControl;
                AddVaryHeaders(context, policy);
                AddCacheStatusHeader(context, "HIT");

                return;
            }
        }

        // For GET requests, we need to buffer the response to generate ETag
        if (context.Request.Method == HttpMethods.Get && policy.GenerateETag)
        {
            await HandleGetRequestWithETag(context, policy, cacheKey);
        }
        else
        {
            // For non-GET or non-ETag requests, just add headers and pass through
            await _next(context);
            AddCacheHeaders(context, policy, null);
        }
    }

    private async Task HandleGetRequestWithETag(HttpContext context, CachePolicyResult policy, string cacheKey)
    {
        // Store original response body stream
        var originalBodyStream = context.Response.Body;

        try
        {
            // Create a memory stream to capture the response
            using var memoryStream = new MemoryStream();
            context.Response.Body = memoryStream;

            // Execute the rest of the pipeline
            await _next(context);

            // Only process successful responses
            if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
            {
                // Get the response body
                memoryStream.Seek(0, SeekOrigin.Begin);
                var responseBody = memoryStream.ToArray();

                // Generate ETag from response body
                var etag = _etagGenerator.GenerateETag(responseBody);

                // Store ETag in cache
                var cacheDuration = TimeSpan.FromSeconds(policy.MaxAge > 0 ? policy.MaxAge : 300);
                await _etagGenerator.StoreETagAsync(cacheKey, etag, cacheDuration);

                // Add cache headers
                AddCacheHeaders(context, policy, etag);

                // Check if client already has this version
                var ifNoneMatch = context.Request.Headers[HeaderNames.IfNoneMatch].FirstOrDefault();
                if (!string.IsNullOrEmpty(ifNoneMatch) && _etagGenerator.ValidateETag(ifNoneMatch, etag))
                {
                    // Client has current version, return 304
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.Headers[HeaderNames.ETag] = etag;
                    AddCacheStatusHeader(context, "HIT");

                    // Copy headers but not body to original stream
                    context.Response.Body = originalBodyStream;
                    return;
                }

                AddCacheStatusHeader(context, "MISS");

                // Copy the response body to the original stream
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(originalBodyStream);
            }
            else
            {
                // Error response - don't cache
                context.Response.Headers[HeaderNames.CacheControl] = "no-store";
                AddCacheStatusHeader(context, "BYPASS");

                // Copy the response to original stream
                memoryStream.Seek(0, SeekOrigin.Begin);
                await memoryStream.CopyToAsync(originalBodyStream);
            }
        }
        finally
        {
            // Restore original body stream
            context.Response.Body = originalBodyStream;
        }
    }

    private CachePolicyResult? GetEffectiveCachePolicy(HttpContext context)
    {
        // First, check for attribute-based policy on the endpoint
        var endpoint = context.GetEndpoint();
        if (endpoint != null)
        {
            var attribute = endpoint.Metadata.GetMetadata<HttpCachePolicyAttribute>();
            if (attribute != null)
            {
                return new CachePolicyResult
                {
                    CacheControl = attribute.BuildCacheControlHeader(),
                    MaxAge = attribute.MaxAge,
                    NoStore = attribute.NoStore,
                    NoCache = attribute.NoCache,
                    IsPrivate = attribute.Private,
                    VaryHeaders = attribute.GetVaryHeaders().ToList(),
                    GenerateETag = _options.ETagEnabled && !attribute.NoStore,
                    PolicySource = "attribute"
                };
            }
        }

        // Fall back to configuration-based policy
        return _policyProvider.GetCachePolicy(context);
    }

    private void AddCacheHeaders(HttpContext context, CachePolicyResult policy, string? etag)
    {
        // Don't overwrite if already set
        if (!context.Response.Headers.ContainsKey(HeaderNames.CacheControl))
        {
            context.Response.Headers[HeaderNames.CacheControl] = policy.CacheControl;
        }

        if (!string.IsNullOrEmpty(etag) && !context.Response.Headers.ContainsKey(HeaderNames.ETag))
        {
            context.Response.Headers[HeaderNames.ETag] = etag;
        }

        // For no-store responses, add Expires and Pragma headers for maximum compatibility
        // with older browsers and HTTP/1.0 proxies
        if (policy.NoStore)
        {
            context.Response.Headers["Expires"] = "0";
            context.Response.Headers["Pragma"] = "no-cache";
        }

        // NOTE: Removed automatic Last-Modified header setting as it was always set to
        // current time which is incorrect. Last-Modified should only be set when we
        // know the actual modification time of the resource.

        AddVaryHeaders(context, policy);
    }

    private static void AddVaryHeaders(HttpContext context, CachePolicyResult policy)
    {
        if (policy.VaryHeaders.Count > 0)
        {
            var existingVary = context.Response.Headers[HeaderNames.Vary].FirstOrDefault();
            var varyHeaders = policy.VaryHeaders.ToList();

            if (!string.IsNullOrEmpty(existingVary))
            {
                var existingHeaders = existingVary.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                varyHeaders.AddRange(existingHeaders);
            }

            context.Response.Headers[HeaderNames.Vary] = string.Join(", ", varyHeaders.Distinct());
        }
    }

    private void AddCacheStatusHeader(HttpContext context, string status)
    {
        if (_options.AddCacheStatusHeader)
        {
            context.Response.Headers["X-Cache-Status"] = status;
        }
    }

    private static string GenerateCacheKey(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        var query = context.Request.QueryString.Value ?? "";

        // Get actual user ID from JWT claims (sub claim is standard, NameIdentifier is fallback)
        var userId = context.User?.FindFirst("sub")?.Value
                  ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? "anonymous";

        // Include user ID for private resources
        return $"{path}{query}:{userId}";
    }
}
