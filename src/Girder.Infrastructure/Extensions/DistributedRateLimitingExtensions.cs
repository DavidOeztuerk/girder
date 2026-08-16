using Girder.Abstractions.Caching;
using Girder.Infrastructure.Caching;
using Girder.Infrastructure.HealthChecks;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring distributed rate limiting
/// </summary>
public static class DistributedRateLimitingExtensions
{
    /// <summary>
    /// Adds distributed rate limiting services to the container
    /// </summary>
    public static IServiceCollection AddDistributedRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        string configurationSectionName = DistributedRateLimitingOptions.SectionName)
    {
        // Configure options
        services.Configure<DistributedRateLimitingOptions>(
            configuration.GetSection(configurationSectionName));

        var rateLimitingOptions = configuration
            .GetSection(configurationSectionName)
            .Get<DistributedRateLimitingOptions>() ?? new DistributedRateLimitingOptions();

        // The IDistributedRateLimitStore implementation comes from a provider
        // package — AddRedisRateLimitStore() or AddInMemoryRateLimitStore().

        return services;
    }

    /// <summary>
    /// Uses distributed rate limiting middleware
    /// </summary>
    public static IApplicationBuilder UseDistributedRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<DistributedRateLimitingMiddleware>();
    }

    /// <summary>
    /// Health check for Redis rate limiting
    /// </summary>
    public static IServiceCollection AddRateLimitingHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<RateLimitingHealthCheck>("rate_limiting");

        return services;
    }
}

/// <summary>
/// Circuit breaker wrapper for rate limit store to provide resilience
/// </summary>
public class CircuitBreakerRateLimitStore : IDistributedRateLimitStore
{
    private readonly IDistributedRateLimitStore _inner;
    private readonly IDistributedRateLimitStore _fallback;
    private readonly ILogger<CircuitBreakerRateLimitStore> _logger;
    private readonly CircuitBreakerOptions _options;

    private int _failureCount = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private bool _isOpen = false;
    private readonly object _lock = new object();

    public CircuitBreakerRateLimitStore(
        IDistributedRateLimitStore inner,
        IDistributedRateLimitStore fallback,
        ILogger<CircuitBreakerRateLimitStore> logger,
        CircuitBreakerOptions options)
    {
        _inner = inner;
        _fallback = fallback;
        _logger = logger;
        _options = options;
    }

    public async Task<long> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.GetCountAsync(key, cancellationToken),
            () => _fallback.GetCountAsync(key, cancellationToken));
    }

    public async Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.IncrementAsync(key, expiration, cancellationToken),
            () => _fallback.IncrementAsync(key, expiration, cancellationToken));
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.ExistsAsync(key, cancellationToken),
            () => _fallback.ExistsAsync(key, cancellationToken));
    }

    public async Task<bool> ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.ExpireAsync(key, expiration, cancellationToken),
            () => _fallback.ExpireAsync(key, expiration, cancellationToken));
    }

    public async Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.GetTimeToLiveAsync(key, cancellationToken),
            () => _fallback.GetTimeToLiveAsync(key, cancellationToken));
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.DeleteAsync(key, cancellationToken),
            () => _fallback.DeleteAsync(key, cancellationToken));
    }

    public async Task<WindowCheckResult> SlidingWindowIncrementAsync(string key, int limit, TimeSpan window, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithCircuitBreaker(
            () => _inner.SlidingWindowIncrementAsync(key, limit, window, cancellationToken),
            () => _fallback.SlidingWindowIncrementAsync(key, limit, window, cancellationToken));
    }

    private async Task<T> ExecuteWithCircuitBreaker<T>(
        Func<Task<T>> primaryOperation,
        Func<Task<T>> fallbackOperation)
    {
        if (!_options.Enabled)
        {
            return await primaryOperation();
        }

        lock (_lock)
        {
            if (_isOpen)
            {
                if (DateTime.UtcNow - _lastFailureTime > _options.OpenTimeout)
                {
                    _isOpen = false;
                    _failureCount = 0;
                    _logger.LogInformation("Circuit breaker reset, attempting primary operation");
                }
                else
                {
                    return HandleOpenCircuit(fallbackOperation);
                }
            }
        }

        try
        {
            using var cts = new CancellationTokenSource(_options.OperationTimeout);
            var result = await primaryOperation();

            // Reset failure count on success
            lock (_lock)
            {
                _failureCount = 0;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary rate limit operation failed");

            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_failureCount >= _options.FailureThreshold)
                {
                    _isOpen = true;
                    _logger.LogWarning("Circuit breaker opened due to {FailureCount} failures", _failureCount);
                }
            }

            return await HandleFailure(fallbackOperation, ex);
        }
    }

    private T HandleOpenCircuit<T>(Func<Task<T>> fallbackOperation)
    {
        return _options.FallbackBehavior switch
        {
            CircuitBreakerFallback.AllowAll => GetDefaultAllowResult<T>(),
            CircuitBreakerFallback.DenyAll => GetDefaultDenyResult<T>(),
            CircuitBreakerFallback.UseInMemory => fallbackOperation().GetAwaiter().GetResult(),
            _ => GetDefaultAllowResult<T>()
        };
    }

    private async Task<T> HandleFailure<T>(Func<Task<T>> fallbackOperation, Exception ex)
    {
        return _options.FallbackBehavior switch
        {
            CircuitBreakerFallback.AllowAll => GetDefaultAllowResult<T>(),
            CircuitBreakerFallback.DenyAll => throw ex,
            CircuitBreakerFallback.UseInMemory => await fallbackOperation(),
            _ => GetDefaultAllowResult<T>()
        };
    }

    private static T GetDefaultAllowResult<T>()
    {
        if (typeof(T) == typeof(bool))
            return (T)(object)true;
        if (typeof(T) == typeof(long))
            return (T)(object)0L;
        if (typeof(T) == typeof(WindowCheckResult))
            return (T)(object)new WindowCheckResult { IsAllowed = true, CurrentCount = 0, Limit = int.MaxValue };

        return default(T)!;
    }

    private static T GetDefaultDenyResult<T>()
    {
        if (typeof(T) == typeof(bool))
            return (T)(object)false;
        if (typeof(T) == typeof(long))
            return (T)(object)long.MaxValue;
        if (typeof(T) == typeof(WindowCheckResult))
            return (T)(object)new WindowCheckResult { IsAllowed = false, CurrentCount = long.MaxValue, Limit = 0 };

        return default(T)!;
    }


}