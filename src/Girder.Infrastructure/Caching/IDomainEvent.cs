namespace Girder.Infrastructure.Caching;

/// <summary>
/// An event that cache invalidation rules can be registered for.
/// </summary>
public interface IDomainEvent
{
    string Id { get; }

    DateTime OccurredAt { get; }
}
