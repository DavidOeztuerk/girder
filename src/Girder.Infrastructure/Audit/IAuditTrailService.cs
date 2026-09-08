namespace Girder.Infrastructure.Audit;

/// <summary>
/// Produces revision-safe audit events with SHA-256 hash chaining.
/// </summary>
/// <remarks>
/// Each event carries the hash of the previous one, so removing or altering an
/// entry breaks the chain and is detectable by any consumer that walks it.
/// <para>
/// The service holds the previous hash in memory. Where several instances run,
/// the sink is responsible for ordering — not the service, because ordering
/// across processes needs a store, and this library names no store.
/// </para>
/// </remarks>
public interface IAuditTrailService
{
    /// <summary>
    /// Records a state change and writes it through the registered
    /// <see cref="ISovereignAuditSink"/>.
    /// </summary>
    /// <typeparam name="T">The resource type.</typeparam>
    /// <param name="actorId">Who performed the action.</param>
    /// <param name="capacity">The capacity (as self, for company …).</param>
    /// <param name="action">What happened (Created, Updated, Deleted, …).</param>
    /// <param name="resource">Which resource was affected.</param>
    /// <param name="before">The state before the change, or <c>default</c> for creates.</param>
    /// <param name="after">The state after the change, or <c>default</c> for deletes.</param>
    /// <param name="correlationId">Optional correlation id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The persisted event, with its hash.</returns>
    Task<AuditEvent<T>> RecordAsync<T>(
        string actorId,
        string capacity,
        string action,
        string resource,
        T? before = default,
        T? after = default,
        string? correlationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a state change with a strongly-typed <see cref="Girder.Core.Identity.Capacity"/>.
    /// </summary>
    Task<AuditEvent<T>> RecordAsync<T>(
        string actorId,
        Girder.Core.Identity.Capacity capacity,
        string action,
        string resource,
        T? before = default,
        T? after = default,
        string? correlationId = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(actorId, capacity.ToString()!, action, resource, before, after, correlationId, cancellationToken);
}
