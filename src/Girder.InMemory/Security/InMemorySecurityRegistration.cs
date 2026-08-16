using Girder.Abstractions.Security;
using Girder.Abstractions.Security.Audit;
using Girder.Abstractions.Security.Authorization;
using Girder.Abstractions.Security.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.InMemory.Security;

public static class InMemorySecurityRegistration
{
    /// <summary>
    /// Keeps secrets in this process. For tests and single-instance runs;
    /// nothing survives a restart.
    /// </summary>
    public static IServiceCollection AddInMemorySecretManager(this IServiceCollection services) =>
        services.AddSingleton<ISecretManager, InMemorySecretManager>();

    /// <summary>
    /// Keeps the audit trail in this process.
    /// </summary>
    /// <remarks>
    /// Entries are lost on restart and invisible to other instances, so this is
    /// not an audit trail anyone can rely on afterwards. Sound for tests.
    /// </remarks>
    public static IServiceCollection AddInMemorySecurityAudit(this IServiceCollection services) =>
        services.AddSingleton<ISecurityAuditService, InMemorySecurityAuditService>();

    /// <summary>
    /// Keeps resource permissions in this process. A second instance grants
    /// nothing that this one granted.
    /// </summary>
    public static IServiceCollection AddInMemoryResourceAuthorization(this IServiceCollection services) =>
        services.AddSingleton<IResourceAuthorizationService, InMemoryResourceAuthorizationService>();

    /// <summary>
    /// Counts rate limits per process.
    /// </summary>
    /// <remarks>
    /// With more than one instance each counts separately, so the effective
    /// limit is the configured one multiplied by the replica count.
    /// </remarks>
    public static IServiceCollection AddInMemoryRateLimiting(this IServiceCollection services) =>
        services.AddSingleton<IRateLimitService, InMemoryRateLimitService>();
}
