using MediatR;
using Microsoft.Extensions.Logging;
using Girder.Cqrs.Interfaces;

namespace Girder.Cqrs.Behaviors;

/// <summary>
/// Audit behavior for commands
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public class AuditBehavior<TRequest, TResponse>(
    ILogger<AuditBehavior<TRequest, TResponse>> logger) 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger = logger;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Only audit commands — check if request implements ICommand<> (any type parameter)
        var isCommand = request.GetType().GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
        if (!isCommand)
        {
            return await next(cancellationToken);
        }

        var requestName = typeof(TRequest).Name;
        var userId = "System";

        if (request is IAuditableCommand auditableCommand)
        {
            userId = auditableCommand.UserId ?? "Anonymous";
        }

        _logger.LogInformation("Audit: User {UserId} executing command {CommandName} at {Timestamp}",
            userId, requestName, DateTime.UtcNow);

        var response = await next(cancellationToken);

        _logger.LogInformation("Audit: Command {CommandName} completed successfully for user {UserId}",
            requestName, userId);

        return response;
    }
}