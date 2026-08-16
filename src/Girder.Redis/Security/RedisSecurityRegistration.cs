using Girder.Abstractions.Security.Audit;
using Girder.Abstractions.Security.Authorization;
using Girder.Redis.Security.Audit;
using Girder.Redis.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;

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
}
