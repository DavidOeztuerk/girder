using Infrastructure.Caching;
using Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace Infrastructure.Middleware;

/// <summary>
/// Distributed rate limiting middleware using Redis or in-memory fallback
/// </summary>
public class DistributedRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IDistributedRateLimitStore _rateLimitStore;
    private readonly ILogger<DistributedRateLimitingMiddleware> _logger;
    private readonly DistributedRateLimitingOptions _options;

    public DistributedRateLimitingMiddleware(
        RequestDelegate next,
        IDistributedRateLimitStore rateLimitStore,
        ILogger<DistributedRateLimitingMiddleware> logger,
        IOptions<DistributedRateLimitingOptions> options)
    {
        _next = next;
        _rateLimitStore = rateLimitStore;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var clientId = GetClientIdentifier(context);
        var endpoint = GetEndpointIdentifier(context);

        if (IsWhitelisted(context, clientId))
        {
            await _next(context);
            return;
        }

        var rateLimitCheck = await CheckRateLimitsAsync(clientId, endpoint, context.Request.Path);

        if (rateLimitCheck.IsAllowed)
        {
            AddRateLimitHeaders(context, rateLimitCheck);
            await _next(context);
        }
        else
        {
            await HandleRateLimitExceeded(context, rateLimitCheck);
        }
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Try to get user ID first (for authenticated requests)
        if (_options.EnableUserRateLimiting && context.User?.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("sub")?.Value
                         ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                return $"user:{userId}";
            }
        }

        // Fall back to IP address
        if (_options.EnableIpRateLimiting)
        {
            var ipAddress = GetClientIpAddress(context);
            return $"ip:{ipAddress}";
        }

        return "anonymous";
    }

    private string GetClientIpAddress(HttpContext context)
    {
        // Check X-Forwarded-For header first (for load balancer/proxy scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0)
            {
                return ips[0].Trim();
            }
        }

        // Check X-Real-IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private string GetEndpointIdentifier(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? "";

        // Normalize path for rate limiting (remove IDs and query parameters)
        var normalizedPath = NormalizePath(path);

        return $"{method}:{normalizedPath}";
    }

    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Replace common ID patterns with placeholders
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>();

        foreach (var segment in segments)
        {
            // Check if segment looks like an ID (GUID, number, etc.)
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

        // Check for number
        if (long.TryParse(segment, out _))
            return true;

        // Check for common ID patterns (must contain at least one digit to distinguish from path words)
        if (segment.Length > 10
            && segment.Any(char.IsDigit)
            && segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            return true;

        return false;
    }

    private bool IsWhitelisted(HttpContext context, string clientId)
    {
        // Check IP whitelist
        var ipAddress = GetClientIpAddress(context);
        if (_options.WhitelistedIps.Contains(ipAddress))
        {
            return true;
        }

        // Check user whitelist
        if (clientId.StartsWith("user:"))
        {
            var userId = clientId.Substring(5);
            if (_options.WhitelistedUserIds.Contains(userId))
            {
                return true;
            }
        }

        // Check endpoint whitelist
        var endpoint = GetEndpointIdentifier(context);
        if (_options.WhitelistedEndpoints.Contains(endpoint))
        {
            return true;
        }

        return false;
    }

    private async Task<CombinedRateLimitResult> CheckRateLimitsAsync(string clientId, string endpoint, string path)
    {
        var defaultLimits = GetDefaultLimits();
        var endpointSpecificLimits = _options.EnableEndpointSpecificLimiting
            ? GetEndpointSpecificLimits(path)
            : null;
        var responseLimits = endpointSpecificLimits ?? defaultLimits;
        var now = DateTime.UtcNow;

        // Create rate limit keys for different time windows
        var keyLimits = new Dictionary<string, (int limit, TimeSpan window)>();

        if (defaultLimits.RequestsPerMinute > 0)
        {
            var minuteKey = $"rl:{clientId}:min:{now:yyyy-MM-dd-HH-mm}";
            keyLimits[minuteKey] = (defaultLimits.RequestsPerMinute, TimeSpan.FromMinutes(1));
        }

        if (defaultLimits.RequestsPerHour > 0)
        {
            var hourKey = $"rl:{clientId}:hour:{now:yyyy-MM-dd-HH}";
            keyLimits[hourKey] = (defaultLimits.RequestsPerHour, TimeSpan.FromHours(1));
        }

        if (defaultLimits.RequestsPerDay > 0)
        {
            var dayKey = $"rl:{clientId}:day:{now:yyyy-MM-dd}";
            keyLimits[dayKey] = (defaultLimits.RequestsPerDay, TimeSpan.FromDays(1));
        }

        if (endpointSpecificLimits is not null)
        {
            if (endpointSpecificLimits.RequestsPerMinute > 0)
            {
                var endpointMinuteKey = $"rl:{clientId}:endpoint:{endpoint}:min:{now:yyyy-MM-dd-HH-mm}";
                keyLimits[endpointMinuteKey] = (endpointSpecificLimits.RequestsPerMinute, TimeSpan.FromMinutes(1));
            }

            if (endpointSpecificLimits.RequestsPerHour > 0)
            {
                var endpointHourKey = $"rl:{clientId}:endpoint:{endpoint}:hour:{now:yyyy-MM-dd-HH}";
                keyLimits[endpointHourKey] = (endpointSpecificLimits.RequestsPerHour, TimeSpan.FromHours(1));
            }

            if (endpointSpecificLimits.RequestsPerDay > 0)
            {
                var endpointDayKey = $"rl:{clientId}:endpoint:{endpoint}:day:{now:yyyy-MM-dd}";
                keyLimits[endpointDayKey] = (endpointSpecificLimits.RequestsPerDay, TimeSpan.FromDays(1));
            }
        }

        // Execute rate limit checks
        var results = new Dictionary<string, Caching.RateLimitResult>();

        foreach (var kvp in keyLimits)
        {
            var key = kvp.Key;
            var (limit, window) = kvp.Value;

            try
            {
                Caching.RateLimitResult result;

                if (_options.UseSlidingWindow)
                {
                    result = await _rateLimitStore.SlidingWindowIncrementAsync(key, limit, window);
                }
                else
                {
                    // Use fixed window increment (custom method)
                    var currentCount = await _rateLimitStore.IncrementAsync(key, window);
                    result = new Caching.RateLimitResult
                    {
                        IsAllowed = currentCount <= limit,
                        CurrentCount = currentCount,
                        Limit = limit,
                        ResetTime = window
                    };
                }

                results[key] = result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rate limit check failed for key {Key}, allowing request", key);

                // Allow request on error to prevent service disruption
                results[key] = new Caching.RateLimitResult
                {
                    IsAllowed = true,
                    CurrentCount = 0,
                    Limit = limit,
                    ResetTime = window
                };
            }
        }

        return new CombinedRateLimitResult
        {
            ClientId = clientId,
            Endpoint = endpoint,
            IsAllowed = results.Values.All(r => r.IsAllowed),
            Results = results,
            Limits = responseLimits
        };
    }

    private EndpointRateLimit GetDefaultLimits() => new()
    {
        RequestsPerMinute = _options.RequestsPerMinute,
        RequestsPerHour = _options.RequestsPerHour,
        RequestsPerDay = _options.RequestsPerDay
    };

    private EndpointRateLimit? GetEndpointSpecificLimits(string path)
    {
        foreach (var kvp in _options.EndpointSpecificLimits)
        {
            var pattern = kvp.Key;
            var limit = kvp.Value;

            if (MatchesPattern(path, pattern))
            {
                return limit;
            }
        }

        return null;
    }

    private static bool MatchesPattern(string path, string pattern)
    {
        // Simple pattern matching - could be enhanced with regex
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private void AddRateLimitHeaders(HttpContext context, CombinedRateLimitResult result)
    {
        var headers = context.Response.Headers;

        // Add standard rate limit headers
        headers.TryAdd("X-RateLimit-Limit", result.Limits.RequestsPerMinute.ToString());

        var minuteResult = result.Results.Values.FirstOrDefault(r => r.Limit == result.Limits.RequestsPerMinute);
        if (minuteResult != null)
        {
            headers.TryAdd("X-RateLimit-Remaining", minuteResult.RemainingRequests.ToString());

            if (minuteResult.ResetTime.HasValue)
            {
                var resetTimestamp = DateTimeOffset.UtcNow.Add(minuteResult.ResetTime.Value).ToUnixTimeSeconds();
                headers.TryAdd("X-RateLimit-Reset", resetTimestamp.ToString());
            }
        }

        // Add retry-after header if rate limited
        if (!result.IsAllowed)
        {
            var retryAfter = CalculateRetryAfter(result);
            headers.TryAdd("Retry-After", retryAfter.ToString());
        }
    }

    private async Task HandleRateLimitExceeded(HttpContext context, CombinedRateLimitResult result)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        var retryAfter = CalculateRetryAfter(result);
        context.Response.Headers.TryAdd("Retry-After", retryAfter.ToString());

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc6585#section-4",
            title = "Too many requests",
            status = 429,
            detail = "Rate limit exceeded. Please try again later.",
            instance = context.Request.Path.Value,
            traceId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow,
            retryAfter = retryAfter,
            limits = new
            {
                perMinute = result.Limits.RequestsPerMinute,
                perHour = result.Limits.RequestsPerHour,
                perDay = result.Limits.RequestsPerDay
            }
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);

        _logger.LogWarning("Rate limit exceeded for {ClientId} on {Endpoint}",
            result.ClientId, result.Endpoint);
    }

    private int CalculateRetryAfter(CombinedRateLimitResult result)
    {
        var minRetryTime = TimeSpan.MaxValue;

        foreach (var rateLimitResult in result.Results.Values)
        {
            if (!rateLimitResult.IsAllowed && rateLimitResult.ResetTime.HasValue)
            {
                if (rateLimitResult.ResetTime.Value < minRetryTime)
                {
                    minRetryTime = rateLimitResult.ResetTime.Value;
                }
            }
        }

        return minRetryTime == TimeSpan.MaxValue ? 60 : (int)minRetryTime.TotalSeconds;
    }
}

/// <summary>
/// Combined result from multiple rate limit checks
/// </summary>
public record CombinedRateLimitResult
{
    public required string ClientId { get; init; }
    public required string Endpoint { get; init; }
    public required bool IsAllowed { get; init; }
    public required Dictionary<string, Caching.RateLimitResult> Results { get; init; }
    public required EndpointRateLimit Limits { get; init; }
}
