namespace Girder.Infrastructure.Audit;

/// <summary>
/// Writes audit events to an external, sovereign sink (database, event store,
/// OpenSearch, …). The sink is provided by whoever operates the system.
/// </summary>
/// <remarks>
/// Girder ships no implementation — a sink names a destination, and that
/// decision belongs to the operator, not to the library. Register one of your
/// own, or use the in-memory implementation for tests.
/// </remarks>
public interface ISovereignAuditSink
{
    /// <summary>
    /// Persists a single audit event.
    /// </summary>
    /// <typeparam name="T">The resource type the event describes.</typeparam>
    /// <param name="auditEvent">The event, with its hash already computed.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task WriteAsync<T>(AuditEvent<T> auditEvent, CancellationToken cancellationToken = default);
}
