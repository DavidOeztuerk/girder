using Girder.Abstractions.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.InMemory.Caching;

public static class InMemoryCacheRegistration
{
    /// <summary>
    /// Keeps the cache and the rate limit counters in this process.
    /// </summary>
    /// <remarks>
    /// A second instance shares none of it: entries cached by one replica are
    /// invisible to the others, and a rate limit counts per process rather than
    /// per application. Sound for a single instance and for tests.
    /// </remarks>
    public static IServiceCollection AddInMemoryCache(this IServiceCollection services, string keyPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        services.AddMemoryCache();

        services.AddSingleton<IDistributedCacheService>(sp => new InMemoryDistributedCacheService(
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<ILogger<InMemoryDistributedCacheService>>(),
            keyPrefix: $"{keyPrefix.ToLowerInvariant()}:"));

        services.AddSingleton<IDistributedRateLimitStore, InMemoryRateLimitStore>();

        return services;
    }
}
