namespace Infrastructure.Extensions;

/// <summary>
/// Extension methods for structured logging
/// </summary>
public static class LoggerExtensions
{
    public static void LogUserAction(this Serilog.ILogger logger, string userId, string action, object? details = null)
    {
        logger.Information("User Action: {UserId} performed {Action} with details {@Details}",
            userId, action, details);
    }

    public static void LogBusinessEvent(this Serilog.ILogger logger, string eventName, object eventData)
    {
        logger.Information("Business Event: {EventName} with data {@EventData}",
            eventName, eventData);
    }

    public static void LogPerformanceMetric(this Serilog.ILogger logger, string operation, long elapsedMs, object? context = null)
    {
        if (elapsedMs > 5000) // Log as warning if operation takes more than 5 seconds
        {
            logger.Warning("Slow Operation: {Operation} took {ElapsedMs}ms with context {@Context}",
                operation, elapsedMs, context);
        }
        else
        {
            logger.Information("Performance: {Operation} completed in {ElapsedMs}ms",
                operation, elapsedMs);
        }
    }

    public static void LogSecurityEvent(this Serilog.ILogger logger, string eventType, string? userId = null, object? details = null)
    {
        logger.Warning("Security Event: {EventType} for user {UserId} with details {@Details}",
            eventType, userId ?? "Anonymous", details);
    }
}
