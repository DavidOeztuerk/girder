using MediatR;
using Microsoft.Extensions.Logging;
using CQRS.Interfaces;

namespace CQRS.Handlers;

/// <summary>
/// Base domain event handler
/// </summary>
/// <typeparam name="TDomainEvent">Domain event type</typeparam>
public abstract class BaseDomainEventHandler<TDomainEvent>(ILogger logger)
    : INotificationHandler<TDomainEvent>
    where TDomainEvent : IDomainEvent
{
    protected readonly ILogger Logger = logger;

    public async Task Handle(TDomainEvent notification, CancellationToken cancellationToken)
    {
        Logger.LogInformation("Handling domain event {EventType} with ID {EventId}",
            typeof(TDomainEvent).Name, notification.EventId);

        try
        {
            await HandleDomainEvent(notification, cancellationToken);

            Logger.LogInformation("Successfully handled domain event {EventType} with ID {EventId}",
                typeof(TDomainEvent).Name, notification.EventId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling domain event {EventType} with ID {EventId}",
                typeof(TDomainEvent).Name, notification.EventId);
            throw;
        }
    }

    protected abstract Task HandleDomainEvent(TDomainEvent domainEvent, CancellationToken cancellationToken);
}