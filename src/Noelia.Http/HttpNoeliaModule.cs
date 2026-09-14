using Microsoft.Extensions.DependencyInjection;
using Noelia.Abstractions.Caching;
using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.RateLimiting;

namespace Noelia.Http;

/// <summary>Exposes the single-instance HTTP rate-limit store in the composition.</summary>
public static class HttpNoeliaModule
{
    public static NoeliaModule InProcessRateLimits => new("Http.InProcessRateLimits");

    public static NoeliaBuilder UseInProcessRateLimits(this NoeliaBuilder noelia)
    {
        ArgumentNullException.ThrowIfNull(noelia);

        return noelia.Use(
            InProcessRateLimits,
            builder =>
            {
                builder.Services.AddMemoryCache();
                builder.Services.AddSingleton<IDistributedRateLimitStore, InProcessRateLimitStore>();
            },
            contract => contract.Provides<IDistributedRateLimitStore>(
                "Noelia.Http", "UseInProcessRateLimits()"));
    }
}
