namespace Girder.Infrastructure.Audit;

/// <summary>
/// In-memory sovereign audit sink for tests and local development.
/// </summary>
public sealed class InMemorySovereignAuditSink : ISovereignAuditSink
{
    private readonly List<object> _events = [];
    private readonly object _lock = new();

    /// <inheritdoc />
    public Task WriteAsync<T>(AuditEvent<T> auditEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        lock (_lock)
        {
            _events.Add(auditEvent);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// All recorded audit events in chronological order.
    /// </summary>
    public IReadOnlyList<object> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    /// <summary>
    /// Returns recorded events of type <typeparamref name="T"/>.
    /// </summary>
    public IReadOnlyList<AuditEvent<T>> EventsOf<T>()
    {
        lock (_lock)
        {
            return _events.OfType<AuditEvent<T>>().ToList();
        }
    }
}
