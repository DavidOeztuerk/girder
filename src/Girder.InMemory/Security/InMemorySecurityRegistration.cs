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
    /// Keeps token revocations in this process, serving both the reading and
    /// the writing side from one instance.
    /// </summary>
    /// <remarks>
    /// Registered as a single object on purpose: the evaluator answers from the
    /// state the writer records, and two instances would revoke nothing while
    /// looking correctly wired.
    /// <para>
    /// Revocations are lost on restart and invisible to other instances, which
    /// means a signed-out token becomes valid again. Use a shared provider
    /// wherever that matters.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddInMemoryTokenRevocation(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryTokenRevocationStore>();
        services.AddSingleton<ITokenRevocationEvaluator>(
            provider => provider.GetRequiredService<InMemoryTokenRevocationStore>());
        services.AddSingleton<ITokenRevocationWriter>(
            provider => provider.GetRequiredService<InMemoryTokenRevocationStore>());
        return services;
    }

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
