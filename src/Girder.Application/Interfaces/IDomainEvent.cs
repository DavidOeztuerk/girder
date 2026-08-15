using MediatR;

namespace Girder.Application.Interfaces;

/// <summary>
/// Interface for domain events
/// </summary>
public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
    string EventId { get; }
}
