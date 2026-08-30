using System.Diagnostics;
using System.Text.RegularExpressions;
using Girder.Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Girder.Infrastructure.Http;

namespace Girder.Infrastructure.Observability;

/// <summary>
/// Middleware for enhanced telemetry and observability
/// </summary>
public partial class TelemetryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TelemetryMiddleware> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly ICustomMetrics _metrics;
    private readonly ObservabilityOptions _observabilityOptions;

    [GeneratedRegex(@"(access_token|token|code|email)=[^&\s]*", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SensitiveQueryParamRegex();

    public TelemetryMiddleware(
        RequestDelegate next,
        ILogger<TelemetryMiddleware> logger,
        ITelemetryService telemetryService,
        ICustomMetrics metrics,
        IOptions<ObservabilityOptions> observabilityOptions)
    {
        _next = next;
        _logger = logger;
        _telemetryService = telemetryService;
        _metrics = metrics;
        _observabilityOptions = observabilityOptions.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestPath = context.Request.Path.Value ?? "";
        var method = context.Request.Method;

        // Start a new activity for this request
        using var activity = _telemetryService.StartActivity($"{method} {requestPath}", ActivityKind.Server);

        // Add standard request tags (redact sensitive query params like access_token)
        var redactedQuery = RedactQueryString(context.Request.QueryString.Value);
        _telemetryService.AddTags(
            new KeyValuePair<string, object?>("http.method", method),
            new KeyValuePair<string, object?>("http.url", $"{context.Request.Scheme}://{context.Request.Host}{context.Request.Path}{redactedQuery}"),
            new KeyValuePair<string, object?>("http.scheme", context.Request.Scheme),
            new KeyValuePair<string, object?>("http.host", context.Request.Host.Value),
            new KeyValuePair<string, object?>("http.path", requestPath),
            new KeyValuePair<string, object?>("http.query", redactedQuery),
            new KeyValuePair<string, object?>("user.id", GetUserId(context)),
            new KeyValuePair<string, object?>("user_agent", context.Request.Headers.UserAgent.FirstOrDefault()),
            new KeyValuePair<string, object?>("client.ip", ClientAddress.Of(context))
        );

        var statusCode = 0;
        Exception? exception = null;

        try
        {
            // Execute the request
            await _next(context);
            statusCode = context.Response.StatusCode;

            // Add response tags
            _telemetryService.AddTags(
                new KeyValuePair<string, object?>("http.status_code", statusCode),
                new KeyValuePair<string, object?>("http.response.content_length", context.Response.ContentLength)
            );

            // Set activity status based on response
            if (statusCode >= 400)
            {
                _telemetryService.SetStatus(ActivityStatusCode.Error, $"HTTP {statusCode}");
            }
            else
            {
                _telemetryService.SetStatus(ActivityStatusCode.Ok);
            }
        }
        catch (Exception ex)
        {
            exception = ex;
            // Don't set statusCode here - it will be set by GlobalExceptionHandler
            // The actual status code will be available in context.Response.StatusCode after re-throw
            
            // Record the exception
            _telemetryService.RecordException(ex);
            _telemetryService.SetStatus(ActivityStatusCode.Error, ex.Message);

            throw;
        }
        finally
        {
            stopwatch.Stop();
            var duration = stopwatch.Elapsed.TotalMilliseconds;
            
            // Get the actual status code from the response (set by GlobalExceptionHandler)
            if (statusCode == 0)
            {
                statusCode = context.Response.StatusCode > 0 ? context.Response.StatusCode : 500;
            }

            // Record metrics
            // _metrics.RecordRequestDuration(duration, requestPath, method);

            // Add duration to activity
            _telemetryService.AddTags(
                new KeyValuePair<string, object?>("http.request.duration_ms", duration)
            );

            // Log request completion
            LogRequestCompletion(context, duration, statusCode, exception);
        }
    }

    private static string? RedactQueryString(string? queryString)
    {
        if (string.IsNullOrEmpty(queryString)) return queryString;

        return SensitiveQueryParamRegex().Replace(queryString, m =>
        {
            var paramName = m.Value.Split('=')[0];
            return $"{paramName}=[REDACTED]";
        });
    }

    private static string? GetUserId(HttpContext context)
    {
        return context.User?.FindFirst("sub")?.Value
               ?? context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
    private void LogRequestCompletion(HttpContext context, double duration, int statusCode, Exception? exception)
    {
        if (ShouldSkipRequestCompletionLog(context.Request.Path))
        {
            return;
        }

        var slowRequestThresholdMs = GetSlowRequestThresholdMs();
        if (statusCode < 400 && duration < slowRequestThresholdMs)
        {
            return;
        }

        var level = statusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ => LogLevel.Information
        };

        var userId = GetUserId(context);
        var clientIp = ClientAddress.Of(context);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = context.TraceIdentifier,
            ["UserId"] = userId,
            ["ClientIP"] = clientIp,
            ["Duration"] = duration,
            ["StatusCode"] = statusCode
        });

        // Don't log exceptions here - GlobalExceptionHandler will handle it
        // Only log the request completion without the exception details
        _logger.Log(level,
            "HTTP {Method} {Path} responded {StatusCode} in {Duration:F2}ms",
            context.Request.Method,
            context.Request.Path,
            statusCode,
            duration);
    }

    private static bool ShouldSkipRequestCompletionLog(PathString path)
    {
        if (!path.HasValue)
        {
            return false;
        }

        var value = path.Value!;
        return value.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase);
    }

    private double GetSlowRequestThresholdMs()
    {
        if (_observabilityOptions.SlowRequestLogThresholdMs > 0)
        {
            return _observabilityOptions.SlowRequestLogThresholdMs;
        }

        return 1000;
    }
}

/// <summary>
/// Extension methods for telemetry middleware
/// </summary>
public static class TelemetryMiddlewareExtensions
{
    /// <summary>
    /// Add telemetry middleware to the pipeline
    /// </summary>
    public static IApplicationBuilder UseTelemetry(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TelemetryMiddleware>();
    }

    /// <summary>
    /// Add correlation ID middleware to the pipeline
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    /// <summary>
    /// Add performance middleware to the pipeline
    /// </summary>
    public static IApplicationBuilder UsePerformance(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PerformanceMiddleware>();
    }
}
