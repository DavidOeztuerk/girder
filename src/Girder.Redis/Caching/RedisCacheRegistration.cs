using Girder.Abstractions.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Girder.Redis.Caching;

public static class RedisCacheRegistration
{
    /// <summary>
    /// Serves the cache and the rate limit store from Redis, so every instance
    /// sees the same state.
    /// </summary>
    /// <param name="keyPrefix">
    /// Separates this application's keys from anything else sharing the server.
    /// </param>
    public static IServiceCollection AddRedisCache(this IServiceCollection services, string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        var prefix = keyPrefix.ToLowerInvariant();

        services.AddSingleton<IDistributedCacheService>(sp => new RedisDistributedCacheService(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            sp.GetRequiredService<ILogger<RedisDistributedCacheService>>(),
            keyPrefix: $"{prefix}:",
            tagPrefix: $"{prefix}:tag:"));

        services.AddSingleton<IDistributedRateLimitStore, RedisDistributedRateLimitStore>();

        return services;
    }
}
