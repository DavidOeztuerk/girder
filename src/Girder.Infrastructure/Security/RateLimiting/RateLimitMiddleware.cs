using Infrastructure.Security.Audit;
using Infrastructure.Security.Monitoring;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace Infrastructure.Security.RateLimiting;

/// <summary>
/// Middleware for API rate limiting
/// </summary>
public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitService _rateLimitService;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly RateLimitOptions _options;

    public RateLimitMiddleware(
        RequestDelegate next,
        IRateLimitService rateLimitService,
        ILogger<RateLimitMiddleware> logger,
        Microsoft.Extensions.Options.IOptions<RateLimitOptions> options)
    {
        _next = next;
        _rateLimitService = rateLimitService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableRateLimiting || ShouldSkipRateLimit(context))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var rateLimitRequest = CreateRateLimitRequest(context);
            var result = await _rateLimitService.CheckRateLimitAsync(rateLimitRequest);

            // Add rate limit headers
            AddRateLimitHeaders(context, result);

            if (!result.IsAllowed)
            {
                try
                {
                    var performanceMetrics = context.RequestServices.GetService<Infrastructure.Observability.IPerformanceMetrics>();
                    performanceMetrics?.RecordRateLimitExceeded("middleware", context.Request.Path, stopwatch.Elapsed.TotalMilliseconds);
                }
                catch
                {
                    // Ignore metrics errors
                }

                await HandleRateLimitExceeded(context, result);
                return;
            }

            // Log successful rate limit check
            if (_options.LogSuccessfulRequests)
            {
                _logger.LogDebug(
                    "Rate limit check passed for {ClientId} on {Method} {Path} - {Used}/{Limit}",
                    rateLimitRequest.ClientId, rateLimitRequest.Method, rateLimitRequest.Path,
                    result.CurrentCount, result.Limit);
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rate limit check");

            // Fail open - allow request if rate limiting fails
            if (_options.FailOpen)
            {
                await _next(context);
            }
            else
            {
                await HandleRateLimitError(context);
            }
        }
        finally
        {
            stopwatch.Stop();

            // Performance metrics recorded inline when rate-limited (above)
        }
    }

    private RateLimitRequest CreateRateLimitRequest(HttpContext context)
    {
        var clientId = GetClientId(context);
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userRoles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();

        return new RateLimitRequest
        {
            ClientId = clientId,
            UserId = userId,
            UserRoles = userRoles,
            Endpoint = GetNormalizedEndpoint(context),
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? "",
            IpAddress = GetClientIpAddress(context),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            RequestSize = context.Request.ContentLength,
            ApiKey = GetApiKey(context),
            Timestamp = DateTime.UtcNow,
            Metadata = GetRequestMetadata(context)
        };
    }

    private string GetClientId(HttpContext context)
    {
        // Priority order for client identification

        // 1. User ID (if authenticated)
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        // 2. API Key
        var apiKey = GetApiKey(context);
        if (!string.IsNullOrEmpty(apiKey))
        {
            return $"apikey:{apiKey}";
        }

        // 3. IP Address
        var ipAddress = GetClientIpAddress(context);
        if (!string.IsNullOrEmpty(ipAddress))
        {
            return $"ip:{ipAddress}";
        }

        // 4. Fallback to session ID or generate
        var sessionId = context.Session?.Id ?? Guid.NewGuid().ToString();
        return $"session:{sessionId}";
    }

    private static string? GetApiKey(HttpContext context)
    {
        // Check Authorization header
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader[7..];
        }

        // Check X-API-Key header
        var apiKeyHeader = context.Request.Headers["X-API-Key"].ToString();
        if (!string.IsNullOrEmpty(apiKeyHeader))
        {
            return apiKeyHeader;
        }

        // Check query parameter
        var apiKeyQuery = context.Request.Query["api_key"].ToString();
        if (!string.IsNullOrEmpty(apiKeyQuery))
        {
            return apiKeyQuery;
        }

        return null;
    }

    private static string GetNormalizedEndpoint(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Normalize by replacing IDs with placeholders
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>();

        foreach (var segment in segments)
        {
            if (IsIdSegment(segment))
            {
                normalizedSegments.Add("{id}");
            }
            else
            {
                normalizedSegments.Add(segment.ToLowerInvariant());
            }
        }

        return "/" + string.Join("/", normalizedSegments);
    }

    private static bool IsIdSegment(string segment)
    {
        // Check for GUID
        if (Guid.TryParse(segment, out _))
            return true;

        // Check for numeric ID
        if (long.TryParse(segment, out _) && segment.Length > 2)
            return true;

        // Check for common ID patterns (alphanumeric, longer than 10 chars)
        if (segment.Length > 10 && segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            return true;

        return false;
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded IP first
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return ips[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static Dictionary<string, object?> GetRequestMetadata(HttpContext context)
    {
        var metadata = new Dictionary<string, object?>();

        // Add request size category
        if (context.Request.ContentLength.HasValue)
        {
            var size = context.Request.ContentLength.Value;
            metadata["request_size_category"] = size switch
            {
                < 1024 => "small",
                < 1024 * 1024 => "medium",
                < 10 * 1024 * 1024 => "large",
                _ => "extra_large"
            };
        }

        // Add content type
        var contentType = context.Request.ContentType;
        if (!string.IsNullOrEmpty(contentType))
        {
            metadata["content_type"] = contentType.Split(';')[0].ToLowerInvariant();
        }

        // Add user agent category
        var userAgent = context.Request.Headers.UserAgent.ToString();
        if (!string.IsNullOrEmpty(userAgent))
        {
            metadata["user_agent_category"] = CategorizeUserAgent(userAgent);
        }

        // Add connection info
        metadata["is_secure"] = context.Request.IsHttps;
        metadata["protocol"] = context.Request.Protocol;

        return metadata;
    }

    private static string CategorizeUserAgent(string userAgent)
    {
        var ua = userAgent.ToLowerInvariant();

        if (ua.Contains("bot") || ua.Contains("crawler") || ua.Contains("spider"))
            return "bot";

        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone"))
            return "mobile";

        if (ua.Contains("postman") || ua.Contains("insomnia") || ua.Contains("curl"))
            return "api_client";

        if (ua.Contains("chrome") || ua.Contains("firefox") || ua.Contains("safari") || ua.Contains("edge"))
            return "browser";

        return "unknown";
    }

    private static void AddRateLimitHeaders(HttpContext context, RateLimitResult result)
    {
        foreach (var header in result.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        // Add standard rate limit headers
        context.Response.Headers["X-RateLimit-Policy"] = "rate-limited";

        if (result.TriggeredRule != null)
        {
            context.Response.Headers["X-RateLimit-Rule"] = result.TriggeredRule.Name;
        }
    }

    private async Task HandleRateLimitExceeded(HttpContext context, RateLimitResult result)
    {
        var clientId = GetClientId(context);
        var ipAddress = GetClientIpAddress(context);
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Log rate limit violation
        _logger.LogWarning(
            "Rate limit exceeded for {ClientId} on {Method} {Path} - {Used}/{Limit}, Rule: {Rule}, Severity: {Severity}",
            clientId, context.Request.Method, context.Request.Path,
            result.CurrentCount, result.Limit, result.TriggeredRule?.Name, result.Severity);

        // Send security alert
        try
        {
            var securityAlertService = context.RequestServices.GetService<ISecurityAlertService>();
            if (securityAlertService != null)
            {
                var alertLevel = result.Severity switch
                {
                    RateLimitSeverity.Critical => SecurityAlertLevel.Critical,
                    RateLimitSeverity.Severe => SecurityAlertLevel.High,
                    RateLimitSeverity.Warning => SecurityAlertLevel.Medium,
                    _ => SecurityAlertLevel.Low
                };

                await securityAlertService.SendAlertAsync(
                    alertLevel,
                    SecurityAlertType.RateLimitExceeded,
                    "Rate Limit Exceeded",
                    $"Rate limit exceeded on {context.Request.Method} {context.Request.Path}. {result.CurrentCount}/{result.Limit} requests in window.",
                    new Dictionary<string, object>
                    {
                        ["ClientId"] = clientId,
                        ["UserId"] = userId ?? "anonymous",
                        ["IpAddress"] = ipAddress,
                        ["Endpoint"] = context.Request.Path.Value ?? "",
                        ["Method"] = context.Request.Method,
                        ["CurrentCount"] = result.CurrentCount,
                        ["Limit"] = result.Limit,
                        ["RuleName"] = result.TriggeredRule?.Name ?? "unknown",
                        ["Severity"] = result.Severity.ToString(),
                        ["UserAgent"] = context.Request.Headers.UserAgent.ToString()
                    },
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send security alert for rate limit violation");
        }

        // Set appropriate status code
        context.Response.StatusCode = result.TriggeredRule?.Actions.CustomStatusCode ?? 429; // Too Many Requests

        // Add Retry-After header
        if (result.RetryAfter.HasValue)
        {
            context.Response.Headers["Retry-After"] = ((int)result.RetryAfter.Value.TotalSeconds).ToString();
        }

        // Apply response delay if configured
        if (result.TriggeredRule?.Actions.ResponseDelay.HasValue == true)
        {
            await Task.Delay(result.TriggeredRule.Actions.ResponseDelay.Value);
        }

        // Add custom headers
        if (result.TriggeredRule?.Actions.ResponseHeaders != null)
        {
            foreach (var header in result.TriggeredRule.Actions.ResponseHeaders)
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }

        // Create error response
        var errorResponse = CreateErrorResponse(result);

        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

        // Execute custom action if defined
        if (result.TriggeredRule?.Actions.CustomAction != null)
        {
            try
            {
                var rateLimitRequest = CreateRateLimitRequest(context);
                await result.TriggeredRule.Actions.CustomAction(rateLimitRequest, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing custom rate limit action");
            }
        }

        // Log to security audit system if available
        try
        {
            var auditService = context.RequestServices.GetService<ISecurityAuditService>();
            if (auditService != null)
            {
                await auditService.LogSecurityEventAsync(
                    "RateLimitExceeded",
                    $"Rate limit exceeded: {result.Reason}",
                    result.Severity switch
                    {
                        RateLimitSeverity.Critical => Audit.SecurityEventSeverity.Critical,
                        RateLimitSeverity.Severe => Audit.SecurityEventSeverity.High,
                        RateLimitSeverity.Warning => Audit.SecurityEventSeverity.Medium,
                        _ => Audit.SecurityEventSeverity.Low
                    },
                    new
                    {
                        ClientId = GetClientId(context),
                        Endpoint = context.Request.Path.Value,
                        Method = context.Request.Method,
                        CurrentCount = result.CurrentCount,
                        Limit = result.Limit,
                        RuleName = result.TriggeredRule?.Name,
                        Severity = result.Severity.ToString()
                    });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log rate limit violation to audit service");
        }
    }

    private async Task HandleRateLimitError(HttpContext context)
    {
        context.Response.StatusCode = 503; // Service Unavailable
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            error = "rate_limit_service_unavailable",
            message = "Rate limiting service is temporarily unavailable",
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private object CreateErrorResponse(RateLimitResult result)
    {
        var response = new Dictionary<string, object?>
        {
            ["error"] = "rate_limit_exceeded",
            ["message"] = result.TriggeredRule?.Actions.CustomMessage ?? result.Reason ?? "Rate limit exceeded",
            ["limit"] = result.Limit,
            ["used"] = result.CurrentCount,
            ["remaining"] = result.Remaining,
            ["reset_time"] = result.ResetTime,
            ["timestamp"] = DateTime.UtcNow
        };

        if (result.RetryAfter.HasValue)
        {
            response["retry_after_seconds"] = (int)result.RetryAfter.Value.TotalSeconds;
        }

        if (_options.IncludeRuleDetails && result.TriggeredRule != null)
        {
            response["rule"] = new
            {
                id = result.TriggeredRule.Id,
                name = result.TriggeredRule.Name,
                description = result.TriggeredRule.Description
            };
        }

        return response;
    }

    private bool ShouldSkipRateLimit(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip for excluded paths
        if (_options.ExcludedPaths.Any(excludedPath =>
            path.StartsWith(excludedPath.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Skip for health checks, metrics, etc.
        if (path.Contains("/health") ||
            path.Contains("/metrics") ||
            path.Contains("/swagger") ||
            path.Contains("/favicon"))
        {
            return true;
        }

        // Skip for OPTIONS requests
        if (context.Request.Method == "OPTIONS")
        {
            return true;
        }

        // Skip for static files
        if (path.EndsWith(".css") ||
            path.EndsWith(".js") ||
            path.EndsWith(".png") ||
            path.EndsWith(".jpg") ||
            path.EndsWith(".gif") ||
            path.EndsWith(".ico"))
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Rate limiting middleware configuration options
/// </summary>
public class RateLimitOptions
{
    /// <summary>
    /// Enable rate limiting middleware
    /// </summary>
    public bool EnableRateLimiting { get; set; } = true;

    /// <summary>
    /// Fail open when rate limiting service is unavailable
    /// </summary>
    public bool FailOpen { get; set; } = true;

    /// <summary>
    /// Log successful rate limit checks
    /// </summary>
    public bool LogSuccessfulRequests { get; set; } = false;

    /// <summary>
    /// Include rule details in error responses
    /// </summary>
    public bool IncludeRuleDetails { get; set; } = false;

    /// <summary>
    /// Paths to exclude from rate limiting
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics",
        "/swagger"
    };

    /// <summary>
    /// Client ID extraction strategy
    /// </summary>
    public ClientIdStrategy ClientIdStrategy { get; set; } = ClientIdStrategy.UserThenApiKeyThenIp;

    /// <summary>
    /// Custom client ID extractor
    /// </summary>
    public Func<HttpContext, string>? CustomClientIdExtractor { get; set; }
}

/// <summary>
/// Client ID extraction strategies
/// </summary>
public enum ClientIdStrategy
{
    /// <summary>
    /// Use IP address only
    /// </summary>
    IpAddressOnly,

    /// <summary>
    /// Use User ID only (authenticated requests)
    /// </summary>
    UserIdOnly,

    /// <summary>
    /// Use API key only
    /// </summary>
    ApiKeyOnly,

    /// <summary>
    /// Use User ID, then API key, then IP address
    /// </summary>
    UserThenApiKeyThenIp,

    /// <summary>
    /// Use API key, then User ID, then IP address
    /// </summary>
    ApiKeyThenUserThenIp,

    /// <summary>
    /// Use custom extractor function
    /// </summary>
    Custom
}

/// <summary>
/// Extension methods for using rate limit middleware
/// </summary>
public static class RateLimitMiddlewareExtensions
{
    /// <summary>
    /// Use rate limiting middleware
    /// </summary>
    public static IApplicationBuilder UseRateLimit(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RateLimitMiddleware>();
    }
}