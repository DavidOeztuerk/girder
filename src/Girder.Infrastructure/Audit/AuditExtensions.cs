using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Girder.Infrastructure.Audit;

/// <summary>
/// Extension methods for registering revision-safe audit trail services.
/// </summary>
public static class AuditExtensions
{
    /// <summary>
    /// Registers <see cref="IAuditTrailService"/> with <see cref="AuditTrailService"/>.
    /// Requires an <see cref="ISovereignAuditSink"/> to be registered, or falls back to
    /// <see cref="InMemorySovereignAuditSink"/> if none is present.
    /// </summary>
    public static IServiceCollection AddSovereignAuditTrail(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ISovereignAuditSink, InMemorySovereignAuditSink>();
        services.TryAddSingleton<IAuditTrailService, AuditTrailService>();

        return services;
    }

    /// <summary>
    /// Registers a custom sovereign audit sink destination.
    /// </summary>
    /// <typeparam name="TSink">The sink implementation.</typeparam>
    public static IServiceCollection AddSovereignAuditSink<TSink>(this IServiceCollection services)
        where TSink : class, ISovereignAuditSink
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISovereignAuditSink, TSink>();
        services.TryAddSingleton<IAuditTrailService, AuditTrailService>();

        return services;
    }
}
