using MassTransit;
using Microsoft.Extensions.Logging;
using Girder.Abstractions.Observability;

namespace Girder.Messaging.MassTransit;

/// <summary>
/// MassTransit consume filter that extracts the correlation ID from incoming
/// message headers and adds it to the logging scope for consumer tracing.
/// </summary>
public class CorrelationIdConsumeFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    private readonly ILogger<CorrelationIdConsumeFilter<T>> _logger;

    public CorrelationIdConsumeFilter(ILogger<CorrelationIdConsumeFilter<T>> logger)
    {
        _logger = logger;
    }

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var correlationId = context.Headers.Get<string>(CorrelationId.HeaderName)
            ?? context.ConversationId?.ToString()
            ?? Guid.NewGuid().ToString();

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationId.BaggageKey] = correlationId,
            ["MessageType"] = typeof(T).Name,
            ["MessageId"] = context.MessageId?.ToString() ?? "unknown"
        });

        _logger.LogDebug("Consuming {MessageType} with correlation ID {CorrelationId}",
            typeof(T).Name, correlationId);

        await next.Send(context);
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("correlationIdConsume");
    }
}
