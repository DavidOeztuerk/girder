namespace Girder.Abstractions.Messaging;

/// <summary>
/// Publishes an event to whoever is listening.
/// </summary>
/// <remarks>
/// Fire-and-forget by design. The port promises that the transport accepted the
/// event, nothing more: it does <b>not</b> promise a transactional outbox, and
/// it does not promise the event survives a crash between the database commit
/// and the publish.
/// <para>
/// Where that guarantee is needed, it belongs to the service that owns the
/// transaction — it writes the intent in the same transaction as the domain
/// change and publishes from there. A delivery guarantee that appears or
/// disappears depending on which transport is configured would be worse than
/// none, because callers would rely on it.
/// </para>
/// </remarks>
public interface IEventBus
{
    /// <summary>Publishes <paramref name="event"/>. Throws if the transport refuses it.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class;
}
