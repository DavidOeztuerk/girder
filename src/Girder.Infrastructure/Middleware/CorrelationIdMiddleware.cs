using System.Diagnostics;
using Girder.Abstractions.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Middleware;

/// <summary>
/// Middleware for adding correlation IDs to requests
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Get correlation ID from header or generate a new one
        var correlationId = GetOrGenerateCorrelationId(context);

        // Add correlation ID to response headers
        context.Response.Headers.TryAdd(CorrelationId.HeaderName, correlationId);

        // Baggage needs an activity to live in, and there is none unless tracing
        // is configured. Losing the correlation id because nobody set up
        // OpenTelemetry would be the wrong way round, so one is started here if
        // it has to be.
        using var own = Activity.Current is null
            ? new Activity(nameof(CorrelationIdMiddleware)).Start()
            : null;

        // As baggage, because that is what crosses a process boundary and what
        // the far end reads; as a tag too, because that is what shows on the span.
        Activity.Current?.AddBaggage(CorrelationId.BaggageKey, correlationId);
        Activity.Current?.SetTag("correlation.id", correlationId);

        // Add correlation ID to log scope
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationId.BaggageKey] = correlationId
        });

        // Store correlation ID in HttpContext for other middleware/controllers
        context.Items[CorrelationId.BaggageKey] = correlationId;

        await _next(context);
    }

    private static string GetOrGenerateCorrelationId(HttpContext context)
    {
        // Check if correlation ID is already present in request headers
        if (context.Request.Headers.TryGetValue(CorrelationId.HeaderName, out var correlationId)
            && !string.IsNullOrEmpty(correlationId))
        {
            return correlationId.ToString();
        }

        // Check trace identifier
        if (!string.IsNullOrEmpty(context.TraceIdentifier))
        {
            return context.TraceIdentifier;
        }

        // Generate a new correlation ID
        return Guid.NewGuid().ToString();
    }
}