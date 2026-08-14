using Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;

namespace Infrastructure.Middleware;

/// <summary>
/// Rate limiting middleware
/// </summary>
public class RateLimitingMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    ILogger<RateLimitingMiddleware> logger,
    IOptions<RateLimitingOptions> options)
{
    private readonly RequestDelegate _next = next;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<RateLimitingMiddleware> _logger = logger;
    private readonly RateLimitingOptions _options = options.Value;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = GetClientIdentifier(context);
        var endpoint = GetEndpointIdentifier(context);

        if (IsWhitelisted(context, clientId))
        {
            await _next(context);
            return;
        }

        var rateLimitCheck = await CheckRateLimitAsync(clientId, endpoint, context.Request.Path);

        if (rateLimitCheck.IsAllowed)
        {
            // Add rate limit headers
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
        var ipAddress = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();

        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(ipAddress))
        {
            ipAddress = context.Connection.RemoteIpAddress?.ToString();
        }

        return ipAddress ?? "unknown";
    }

    private string GetEndpointIdentifier(HttpContext context)
    {
        return $"{context.Request.Method}:{context.Request.Path}";
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

        return false;
    }

    private async Task<RateLimitResult> CheckRateLimitAsync(string clientId, string endpoint, string path)
    {
        var semaphore = _semaphores.GetOrAdd(clientId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            var limits = GetLimitsForEndpoint(path);
            var now = DateTime.UtcNow;

            var minuteKey = $"{clientId}:minute:{now:yyyy-MM-dd-HH-mm}";
            var hourKey = $"{clientId}:hour:{now:yyyy-MM-dd-HH}";
            var dayKey = $"{clientId}:day:{now:yyyy-MM-dd}";

            var minuteCount = GetOrSetCount(minuteKey, TimeSpan.FromMinutes(1));
            var hourCount = GetOrSetCount(hourKey, TimeSpan.FromHours(1));
            var dayCount = GetOrSetCount(dayKey, TimeSpan.FromDays(1));

            var result = new RateLimitResult
            {
                ClientId = clientId,
                Endpoint = endpoint,
                RequestsPerMinute = minuteCount,
                RequestsPerHour = hourCount,
                RequestsPerDay = dayCount,
                LimitsPerMinute = limits.RequestsPerMinute,
                LimitsPerHour = limits.RequestsPerHour,
                LimitsPerDay = limits.RequestsPerDay
            };

            if (minuteCount > limits.RequestsPerMinute ||
                hourCount > limits.RequestsPerHour ||
                dayCount > limits.RequestsPerDay)
            {
                result.IsAllowed = false;

                _logger.LogWarning("Rate limit exceeded for {ClientId} on {Endpoint}. " +
                                 "Minute: {MinuteCount}/{MinuteLimit}, " +
                                 "Hour: {HourCount}/{HourLimit}, " +
                                 "Day: {DayCount}/{DayLimit}",
                    clientId, endpoint, minuteCount, limits.RequestsPerMinute,
                    hourCount, limits.RequestsPerHour, dayCount, limits.RequestsPerDay);
            }
            else
            {
                result.IsAllowed = true;

                // Increment counters
                IncrementCount(minuteKey, TimeSpan.FromMinutes(1));
                IncrementCount(hourKey, TimeSpan.FromHours(1));
                IncrementCount(dayKey, TimeSpan.FromDays(1));
            }

            return result;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private EndpointRateLimit GetLimitsForEndpoint(string path)
    {
        // Check for endpoint-specific limits
        foreach (var endpointLimit in _options.EndpointSpecificLimits.Values)
        {
            if (path.StartsWith(endpointLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                return endpointLimit;
            }
        }

        // Return default limits
        return new EndpointRateLimit
        {
            RequestsPerMinute = _options.RequestsPerMinute,
            RequestsPerHour = _options.RequestsPerHour,
            RequestsPerDay = _options.RequestsPerDay
        };
    }

    private int GetOrSetCount(string key, TimeSpan expiration)
    {
        if (_cache.TryGetValue(key, out int count))
        {
            return count;
        }

        _cache.Set(key, 0, expiration);
        return 0;
    }

    private void IncrementCount(string key, TimeSpan expiration)
    {
        var count = GetOrSetCount(key, expiration);
        _cache.Set(key, count + 1, expiration);
    }

    private void AddRateLimitHeaders(HttpContext context, RateLimitResult result)
    {
        context.Response.Headers.TryAdd("X-RateLimit-Limit-Minute", result.LimitsPerMinute.ToString());
        context.Response.Headers.TryAdd("X-RateLimit-Remaining-Minute",
            Math.Max(0, result.LimitsPerMinute - result.RequestsPerMinute).ToString());

        context.Response.Headers.TryAdd("X-RateLimit-Limit-Hour", result.LimitsPerHour.ToString());
        context.Response.Headers.TryAdd("X-RateLimit-Remaining-Hour",
            Math.Max(0, result.LimitsPerHour - result.RequestsPerHour).ToString());

        context.Response.Headers.TryAdd("X-RateLimit-Limit-Day", result.LimitsPerDay.ToString());
        context.Response.Headers.TryAdd("X-RateLimit-Remaining-Day",
            Math.Max(0, result.LimitsPerDay - result.RequestsPerDay).ToString());
    }

    private async Task HandleRateLimitExceeded(HttpContext context, RateLimitResult result)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        var response = new
        {
            error = "Rate limit exceeded",
            message = "Too many requests. Please try again later.",
            details = new
            {
                limits = new
                {
                    perMinute = result.LimitsPerMinute,
                    perHour = result.LimitsPerHour,
                    perDay = result.LimitsPerDay
                },
                current = new
                {
                    perMinute = result.RequestsPerMinute,
                    perHour = result.RequestsPerHour,
                    perDay = result.RequestsPerDay
                }
            },
            retryAfter = CalculateRetryAfter(result)
        };

        var json = System.Text.Json.JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }

    private int CalculateRetryAfter(RateLimitResult result)
    {
        var now = DateTime.UtcNow;

        // If minute limit exceeded, retry after next minute
        if (result.RequestsPerMinute >= result.LimitsPerMinute)
        {
            var nextMinute = now.AddMinutes(1).Date.AddHours(now.Hour).AddMinutes(now.Minute + 1);
            return (int)(nextMinute - now).TotalSeconds;
        }

        // If hour limit exceeded, retry after next hour
        if (result.RequestsPerHour >= result.LimitsPerHour)
        {
            var nextHour = now.Date.AddHours(now.Hour + 1);
            return (int)(nextHour - now).TotalSeconds;
        }

        // If day limit exceeded, retry after next day
        var nextDay = now.Date.AddDays(1);
        return (int)(nextDay - now).TotalSeconds;
    }
}
