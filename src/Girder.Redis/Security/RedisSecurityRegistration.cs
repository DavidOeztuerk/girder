using Girder.Abstractions.Security;
using Girder.Abstractions.Security.Audit;
using Girder.Abstractions.Security.Authorization;
using Girder.Abstractions.Security.RateLimiting;
using Girder.Redis.Security.Audit;
using Girder.Redis.Security.RateLimiting;
using Girder.Redis.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Girder.Redis.Security;

public static class RedisSecurityRegistration
{
    /// <summary>
    /// Writes the security audit trail to Redis, hash-chained so a missing or
    /// altered entry is detectable.
    /// </summary>
    /// <remarks>
    /// An audit trail is only evidence if it outlives the incident it records.
    /// Redis keeps it only as long as its persistence settings allow, so ship
    /// entries onward if they must be retained.
    /// </remarks>
    public static IServiceCollection AddRedisSecurityAudit(this IServiceCollection services) =>
        services.AddSingleton<ISecurityAuditService, SecurityAuditService>();

    /// <summary>
    /// Keeps resource permissions and ownership in Redis, so every instance
    /// reaches the same decision.
    /// </summary>
    public static IServiceCollection AddRedisResourceAuthorization(this IServiceCollection services) =>
        services.AddSingleton<IResourceAuthorizationService, ResourceAuthorizationService>();

    /// <summary>
    /// Counts rate limits in Redis, so all instances share one budget.
    /// </summary>
    public static IServiceCollection AddRedisRateLimiting(this IServiceCollection services) =>
        services.AddSingleton<IRateLimitService, RateLimitService>();

    /// <summary>
    /// Keeps token revocations in Redis, serving both the reading and the
    /// writing side from one instance.
    /// </summary>
    /// <remarks>
    /// Requires a registered <c>IConnectionMultiplexer</c> — call
    /// <c>AddRedisConnection(...)</c> first.
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="maxTokenLifetime">
    /// How long a cutoff is kept. Must be at least the longest lifetime an
    /// access token can have; checked here rather than on first use, so a wrong
    /// value fails at composition instead of on the request that needed it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxTokenLifetime"/> is not positive.
    /// </exception>
    public static IServiceCollection AddRedisTokenRevocation(
        this IServiceCollection services,
        TimeSpan maxTokenLifetime)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxTokenLifetime, TimeSpan.Zero);

        services.AddSingleton(provider => new RedisTokenRevocationStore(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            maxTokenLifetime,
            provider.GetService<TimeProvider>()));
        services.AddSingleton<ITokenRevocationEvaluator>(
            provider => provider.GetRequiredService<RedisTokenRevocationStore>());
        services.AddSingleton<ITokenRevocationWriter>(
            provider => provider.GetRequiredService<RedisTokenRevocationStore>());

        return services;
    }
}
