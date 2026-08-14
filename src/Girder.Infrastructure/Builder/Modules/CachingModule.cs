using Infrastructure.Caching;
using Infrastructure.Caching.Http;
using Infrastructure.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Builder.Modules;

public static class CachingModule
{
    /// <summary>
    /// Add caching with Redis (if available) or in-memory fallback.
    /// Also registers IDistributedCacheService and HTTP response caching.
    /// </summary>
    public static InfrastructureBuilder AddCaching(this InfrastructureBuilder builder)
    {
        builder.CachingEnabled = true;

        var redisConnectionString = builder.RedisConnectionString;
        var serviceName = builder.ServiceName;

        // Core caching (Redis or memory)
        builder.Services.AddCaching(redisConnectionString ?? string.Empty);

        // HTTP response caching
        builder.Services.AddHttpResponseCaching(builder.Configuration);

        // IDistributedCacheService + rate limit store
        var cachePrefix = serviceName.ToLowerInvariant();
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            builder.Services.AddSingleton<IDistributedCacheService>(sp =>
                new RedisDistributedCacheService(
                    sp.GetRequiredService<IConnectionMultiplexer>(),
                    sp.GetRequiredService<ILogger<RedisDistributedCacheService>>(),
                    keyPrefix: $"{cachePrefix}:",
                    tagPrefix: $"{cachePrefix}:tag:"));
            builder.Services.AddSingleton<IDistributedRateLimitStore, RedisDistributedRateLimitStore>();
        }
        else
        {
            builder.Services.AddSingleton<IDistributedCacheService>(sp =>
                new InMemoryDistributedCacheService(
                    sp.GetRequiredService<IMemoryCache>(),
                    sp.GetRequiredService<ILogger<InMemoryDistributedCacheService>>(),
                    keyPrefix: $"{cachePrefix}:"));
            builder.Services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();
        }

        // CacheInvalidationService (skip for Gateway)
        if (!serviceName.Equals("Gateway", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<CacheInvalidationService>();
        }

        return builder;
    }
}
