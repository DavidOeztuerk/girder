using MediatR;
using Microsoft.Extensions.Logging;
using CQRS.Interfaces;
using Infrastructure.Caching;
using System.Text.Json;

namespace CQRS.Behaviors;

/// <summary>
/// Caching behavior for queries
/// Uses IDistributedCacheService (custom) instead of IDistributedCache (Microsoft)
/// to ensure cache invalidation patterns work correctly.
///
/// IMPORTANT: Both CachingBehavior and CacheInvalidationBehavior MUST use
/// the same cache interface (IDistributedCacheService) for pattern-based
/// invalidation to work!
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public class CachingBehavior<TRequest, TResponse>(
    IDistributedCacheService? cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : class
{
    private readonly IDistributedCacheService? _cache = cache;
    private readonly ILogger<CachingBehavior<TRequest, TResponse>> _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only cache if it's a cacheable query and cache is available
        if (_cache == null || request is not ICacheableQuery cacheableQuery)
        {
            return await next(cancellationToken);
        }

        var cacheKey = cacheableQuery.CacheKey;

        // Try to get from cache using IDistributedCacheService
        try
        {
            var cachedResult = await _cache.GetAsync<TResponse>(cacheKey, cancellationToken);
            if (cachedResult != null)
            {
                _logger.LogInformation("Cache hit for key {CacheKey}", cacheKey);
                return cachedResult;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve cached value for key {CacheKey}", cacheKey);
        }

        _logger.LogInformation("Cache miss for key {CacheKey}", cacheKey);

        // Execute handler and cache result
        var response = await next(cancellationToken);

        if (response != null)
        {
            try
            {
                await _cache.SetAsync(
                    cacheKey,
                    response,
                    cacheableQuery.CacheDuration,
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Cached result for key {CacheKey} with duration {Duration}",
                    cacheKey, cacheableQuery.CacheDuration);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cache result for key {CacheKey}", cacheKey);
            }
        }

        return response!;
    }
}
