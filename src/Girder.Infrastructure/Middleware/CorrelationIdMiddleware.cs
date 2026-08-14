using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Middleware;

/// <summary>
/// Middleware for adding correlation IDs to requests
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private const string CorrelationIdHeaderName = "X-Correlation-ID";
    private const string CorrelationIdLogProperty = "CorrelationId";

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
        context.Response.Headers.TryAdd(CorrelationIdHeaderName, correlationId);

        // Add correlation ID to current activity
        Activity.Current?.SetTag("correlation.id", correlationId);

        // Add correlation ID to log scope
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdLogProperty] = correlationId
        });

        // Store correlation ID in HttpContext for other middleware/controllers
        context.Items[CorrelationIdLogProperty] = correlationId;

        await _next(context);
    }

    private static string GetOrGenerateCorrelationId(HttpContext context)
    {
        // Check if correlation ID is already present in request headers
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationId)
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