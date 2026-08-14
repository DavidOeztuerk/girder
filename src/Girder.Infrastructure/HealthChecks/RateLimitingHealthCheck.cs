using Infrastructure.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.HealthChecks;

/// <summary>
/// Health check for rate limiting functionality
/// </summary>
public class RateLimitingHealthCheck : IHealthCheck
{
    private readonly IDistributedRateLimitStore _rateLimitStore;
    private readonly ILogger<RateLimitingHealthCheck> _logger;

    public RateLimitingHealthCheck(
        IDistributedRateLimitStore rateLimitStore,
        ILogger<RateLimitingHealthCheck> logger)
    {
        _rateLimitStore = rateLimitStore;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var testKey = $"healthcheck:{Environment.MachineName}:{Guid.NewGuid()}";
            var testLimit = 10;
            var testWindow = TimeSpan.FromMinutes(1);

            // Test basic functionality
            var startTime = DateTime.UtcNow;
            
            // Test sliding window increment
            var result = await _rateLimitStore.SlidingWindowIncrementAsync(testKey, testLimit, testWindow, cancellationToken);
            
            if (!result.IsAllowed || result.CurrentCount != 1)
            {
                return HealthCheckResult.Degraded("Rate limiting test failed: Unexpected result from sliding window increment");
            }

            // Test basic increment
            var incrementResult = await _rateLimitStore.IncrementAsync($"{testKey}:basic", testWindow, cancellationToken);
            
            if (incrementResult != 1)
            {
                return HealthCheckResult.Degraded("Rate limiting test failed: Unexpected result from basic increment");
            }

            // Test exists check
            var exists = await _rateLimitStore.ExistsAsync($"{testKey}:basic", cancellationToken);
            
            if (!exists)
            {
                return HealthCheckResult.Degraded("Rate limiting test failed: Key should exist after increment");
            }

            // Clean up test keys
            await _rateLimitStore.DeleteAsync(testKey, cancellationToken);
            await _rateLimitStore.DeleteAsync($"{testKey}:basic", cancellationToken);

            var responseTime = DateTime.UtcNow - startTime;

            var data = new Dictionary<string, object>
            {
                ["response_time_ms"] = responseTime.TotalMilliseconds,
                ["store_type"] = _rateLimitStore.GetType().Name,
                ["test_key"] = testKey
            };

            if (responseTime.TotalMilliseconds > 1000)
            {
                _logger.LogWarning("Rate limiting health check slow response: {ResponseTime}ms", responseTime.TotalMilliseconds);
                return HealthCheckResult.Degraded($"Rate limiting response time is slow: {responseTime.TotalMilliseconds:F0}ms", data: data);
            }

            return HealthCheckResult.Healthy("Rate limiting is functioning correctly", data);
        }
        catch (TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("Rate limiting health check timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rate limiting health check failed");
            
            var data = new Dictionary<string, object>
            {
                ["error"] = ex.Message,
                ["store_type"] = _rateLimitStore.GetType().Name
            };

            // If this is a Redis connection issue, it might recover
            if (ex.Message.Contains("Redis") || ex.Message.Contains("connection"))
            {
                return HealthCheckResult.Degraded($"Rate limiting store connection issue: {ex.Message}", ex, data);
            }

            return HealthCheckResult.Unhealthy($"Rate limiting health check failed: {ex.Message}", ex, data);
        }
    }
}