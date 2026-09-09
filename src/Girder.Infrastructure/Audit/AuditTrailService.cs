using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Audit;

/// <summary>
/// Default implementation of <see cref="IAuditTrailService"/> with in-memory
/// hash-chain state.
/// </summary>
/// <remarks>
/// Concurrent calls are serialised end to end — the hash is chained and the
/// event is written to the sink inside the same turn. Advancing the chain alone
/// under a lock is not enough: the writes would then reach the sink in another
/// order than they were chained in, and a verifier reading the store back would
/// find a broken chain on a system where nothing was tampered with.
/// <para>
/// Where several replicas run, each holds its own chain — the sink decides
/// whether that matters.
/// </para>
/// </remarks>
public sealed class AuditTrailService : IAuditTrailService, IDisposable
{
    private readonly ISovereignAuditSink _sink;
    private readonly ILogger<AuditTrailService> _logger;

    // A semaphore rather than a lock: the sink write belongs inside the
    // serialised turn, and it is asynchronous.
    private readonly SemaphoreSlim _chain = new(1, 1);
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

        await _chain.WaitAsync(cancellationToken);
        try
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

            await _sink.WriteAsync(auditEvent, cancellationToken);

            // Only once the sink has it: a chain advanced past an event that was
            // never stored leaves a gap no verifier can close.
            _previousHash = auditEvent.Hash;
        }
        finally
        {
            _chain.Release();
        }

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

    /// <inheritdoc />
    public void Dispose() => _chain.Dispose();
}
