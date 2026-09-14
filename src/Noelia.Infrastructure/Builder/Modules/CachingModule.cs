using Noelia.Infrastructure.Caching;
using Noelia.Infrastructure.Caching.Http;
using Noelia.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Noelia.Infrastructure.Builder.Modules;

public static class CachingModule
{
    /// <summary>
    /// Adds HTTP response caching and cache invalidation.
    /// </summary>
    /// <remarks>
    /// Registers no <c>IDistributedCacheService</c> and no
    /// <c>IDistributedRateLimitStore</c>: where cached data lives decides which
    /// server has to run, which is the operator's call. Follow this with
    /// <c>AddRedisCache(...)</c> or <c>AddInMemoryCache(...)</c> from the
    /// matching provider package.
    /// </remarks>
    public static InfrastructureBuilder AddCaching(this InfrastructureBuilder builder)
    {
        builder.CachingEnabled = true;

        builder.RequiresProvider<Noelia.Abstractions.Caching.IDistributedCacheService>(
            "AddCaching()", "AddRedisCache(prefix) or AddInMemoryCache(prefix)");

        builder.Services.AddCaching();
        builder.Services.AddHttpResponseCaching(builder.Configuration);
        builder.Services.AddSingleton<CacheInvalidationService>();

        return builder;
    }
}
