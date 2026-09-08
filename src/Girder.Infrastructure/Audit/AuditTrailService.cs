using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Audit;

/// <summary>
/// Default implementation of <see cref="IAuditTrailService"/> with in-memory
/// hash-chain state.
/// </summary>
/// <remarks>
/// Thread-safe: the previous hash is advanced under a lock so concurrent calls
/// produce a linear chain. Where several replicas run, each holds its own
/// chain — the sink decides whether that matters.
/// </remarks>
public sealed class AuditTrailService : IAuditTrailService
{
    private readonly ISovereignAuditSink _sink;
    private readonly ILogger<AuditTrailService> _logger;
    private readonly object _chainLock = new();
    private string? _previousHash;

    public AuditTrailService(ISovereignAuditSink sink, ILogger<AuditTrailService> logger)
    {
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AuditEvent<T>> RecordAsync<T>(
        string actorId,
        string capacity,
        string action,
        string resource,
        T? before = default,
        T? after = default,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capacity);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        AuditEvent<T> auditEvent;

        lock (_chainLock)
        {
            auditEvent = new AuditEvent<T>
            {
                ActorId = actorId,
                Capacity = capacity,
                Action = action,
                Resource = resource,
                CorrelationId = correlationId,
                BeforeStateJson = AuditEvent<T>.ToJson(before),
                AfterStateJson = AuditEvent<T>.ToJson(after),
                PreviousHash = _previousHash
            }.WithComputedHash();

            _previousHash = auditEvent.Hash;
        }

        await _sink.WriteAsync(auditEvent, cancellationToken);

        _logger.LogDebug(
            "Audit event {AuditEventId}: {Action} on {Resource} by {ActorId} [{Capacity}]",
            auditEvent.Id, action, resource, actorId, capacity);

        return auditEvent;
    }

    /// <inheritdoc />
    public Task<AuditEvent<T>> RecordAsync<T>(
        string actorId,
        Girder.Core.Identity.Capacity capacity,
        string action,
        string resource,
        T? before = default,
        T? after = default,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capacity);
        return RecordAsync(actorId, capacity.ToString(), action, resource, before, after, correlationId, cancellationToken);
    }
}
