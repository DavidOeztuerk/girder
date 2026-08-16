using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Messaging.MassTransit;

/// <summary>
/// MassTransit publish filter that propagates the correlation ID from the
/// current HTTP context into outgoing integration event headers.
/// This ensures cross-service tracing continuity between HTTP and messaging.
/// </summary>
public class CorrelationIdPublishFilter<T> : IFilter<PublishContext<T>> where T : class
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CorrelationIdPublishFilter<T>> _logger;
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    public CorrelationIdPublishFilter(
        IHttpContextAccessor httpContextAccessor,
        ILogger<CorrelationIdPublishFilter<T>> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();

        if (!string.IsNullOrEmpty(correlationId))
        {
            context.Headers.Set(CorrelationIdHeaderName, correlationId);
            _logger.LogDebug("Propagated correlation ID {CorrelationId} to integration event {EventType}",
                correlationId, typeof(T).Name);
        }

        await next.Send(context);
    }

    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("correlationIdPublish");
    }
}
