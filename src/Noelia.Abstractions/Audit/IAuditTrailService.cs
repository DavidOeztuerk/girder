namespace Noelia.Abstractions.Audit;

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
    /// Records a state change and writes it through the audit sink configured
    /// by the infrastructure package.
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
    /// Reads a bounded, state-free view of this instance's audit chain.
    /// Implementations that cannot inspect their chain report unavailable.
    /// </summary>
    /// <remarks>
    /// Entries contain actor, capacity, action and resource, but never the
    /// before/after snapshots or hash material held by <see cref="AuditEvent{T}"/>.
    /// </remarks>
    Task<AuditTrailInspection> InspectAsync(
        int latest = 20,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AuditTrailInspection.Unavailable);

    /// <summary>
    /// Records a state change with a strongly-typed <see cref="Noelia.Core.Identity.Capacity"/>.
    /// </summary>
    Task<AuditEvent<T>> RecordAsync<T>(
        string actorId,
        Noelia.Core.Identity.Capacity capacity,
        string action,
        string resource,
        T? before = default,
        T? after = default,
        string? correlationId = null,
        CancellationToken cancellationToken = default) =>
        RecordAsync(actorId, capacity.ToString()!, action, resource, before, after, correlationId, cancellationToken);
}

/// <summary>A state-free audit entry safe for an operator view.</summary>
public sealed record AuditTrailEntry(
    DateTimeOffset Timestamp,
    string ActorId,
    string Capacity,
    string Action,
    string Resource);

/// <summary>The bounded audit-chain view for the instance answering the request.</summary>
public sealed record AuditTrailInspection(
    bool IsAvailable,
    long Length,
    bool IsChainValidAtWriteTime,
    bool VerifiesPersistedSink,
    IReadOnlyList<AuditTrailEntry> Latest)
{
    /// <summary>An implementation that records but exposes no read model.</summary>
    public static AuditTrailInspection Unavailable { get; } =
        new(false, 0, false, false, Array.Empty<AuditTrailEntry>());
}
