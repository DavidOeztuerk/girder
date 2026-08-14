namespace Girder.Infrastructure.Messaging;

/// <summary>
/// Abstraction for tracking processed integration events to ensure consumer idempotency.
/// Each service implements this against its own DbContext to enable transactional
/// deduplication — the idempotency check and business write share the same DB transaction.
/// </summary>
public interface IProcessedEventStore
{
    /// <summary>
    /// Checks whether a specific event has already been processed by a given consumer.
    /// </summary>
    /// <param name="eventId">The unique message/event ID (typically from ConsumeContext.MessageId).</param>
    /// <param name="consumerName">The consumer name for disambiguation when multiple consumers handle the same event type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was already processed; false otherwise.</returns>
    Task<bool> HasBeenProcessedAsync(string eventId, string consumerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an event as processed. Must be called within the same transaction as the business write
    /// to ensure atomicity.
    /// </summary>
    /// <param name="eventId">The unique message/event ID.</param>
    /// <param name="consumerName">The consumer name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkAsProcessedAsync(string eventId, string consumerName, CancellationToken cancellationToken = default);
}
