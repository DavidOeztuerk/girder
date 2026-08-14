using Girder.Infrastructure.Extensions;

namespace Girder.Infrastructure.Builder.Modules;

public static class RateLimitingModule
{
    /// <summary>
    /// Add distributed rate limiting (Redis-backed or in-memory fallback).
    /// </summary>
    public static InfrastructureBuilder AddDistributedRateLimiting(this InfrastructureBuilder builder)
    {
        builder.RateLimitingEnabled = true;
        builder.Services.AddDistributedRateLimiting(builder.Configuration);
        return builder;
    }
}
