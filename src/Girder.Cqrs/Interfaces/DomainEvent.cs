namespace CQRS.Interfaces;

/// <summary>
/// Base domain event
/// </summary>
public abstract record DomainEvent : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
    public string EventId { get; } = Guid.NewGuid().ToString();
}