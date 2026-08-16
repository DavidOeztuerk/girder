using Girder.Infrastructure.Caching;
using Girder.Infrastructure.Caching.Http;
using Girder.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

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

        builder.Services.AddCaching();
        builder.Services.AddHttpResponseCaching(builder.Configuration);
        builder.Services.AddSingleton<CacheInvalidationService>();

        return builder;
    }
}
