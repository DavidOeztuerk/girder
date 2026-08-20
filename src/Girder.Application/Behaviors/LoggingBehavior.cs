using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Girder.Core.Logging;
using Girder.Core.Exceptions;
using FluentValidation;

namespace Girder.Application.Behaviors;

/// <summary>
/// Enhanced logging behavior with correlation ID tracking, performance monitoring, and sensitive data sanitization
/// </summary>
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogSanitizer _logSanitizer;
    private const int SlowRequestThresholdMs = 1000;
    private const int CriticalRequestThresholdMs = 5000;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        IHttpContextAccessor httpContextAccessor,
        ILogSanitizer? logSanitizer = null)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _logSanitizer = logSanitizer ?? new LogSanitizer();
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var responseName = typeof(TResponse).Name;
        var correlationId = GetCorrelationId();
        var requestId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        // The shape, not the contents. Sanitising a payload only removes the
        // fields somebody thought of, and a command carries free text — a note,
        // a title, a reason — that is on no list.
        var requestShape = Shape.Of(request);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["RequestId"] = requestId,
            ["CorrelationId"] = correlationId,
            ["RequestName"] = requestName,
            ["ResponseType"] = responseName,
            ["UserId"] = GetUserId() ?? "Anonymous"
        }))
        {
            _logger.LogInformation(
                "Starting request {RequestName} [{RequestId}] with correlation {CorrelationId}",
                requestName, requestId, correlationId);

            _logger.LogDebug(
                "Shape of {RequestName}: {RequestShape}",
                requestName, requestShape);

            try
            {
                var response = await next(cancellationToken);

                stopwatch.Stop();
                LogRequestCompletion(requestName, requestId, correlationId, stopwatch.ElapsedMilliseconds, true);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Shape of the response to {RequestName} [{RequestId}]: {ResponseShape}",
                        requestName, requestId, Shape.Of(response));
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                LogRequestCompletion(requestName, requestId, correlationId, stopwatch.ElapsedMilliseconds, false);

                // Enhanced error logging based on exception type
                LogException(ex, requestName, requestId, correlationId, requestShape);

                throw;
            }
        }
    }

    private void LogRequestCompletion(string requestName, string requestId, string correlationId, 
        long elapsedMs, bool success)
    {
        var logLevel = DetermineLogLevel(elapsedMs, success);
        var status = success ? "completed" : "failed";

        if (elapsedMs > CriticalRequestThresholdMs)
        {
            _logger.Log(logLevel,
                "CRITICAL PERFORMANCE: Request {RequestName} [{RequestId}] {Status} in {ElapsedMs}ms (CorrelationId: {CorrelationId})",
                requestName, requestId, status, elapsedMs, correlationId);
        }
        else if (elapsedMs > SlowRequestThresholdMs)
        {
            _logger.Log(logLevel,
                "SLOW REQUEST: {RequestName} [{RequestId}] {Status} in {ElapsedMs}ms (CorrelationId: {CorrelationId})",
                requestName, requestId, status, elapsedMs, correlationId);
        }
        else
        {
            _logger.Log(logLevel,
                "Request {RequestName} [{RequestId}] {Status} in {ElapsedMs}ms",
                requestName, requestId, status, elapsedMs);
        }
    }

    /// <summary>Records a failure, with the shape of what caused it.</summary>
    /// <param name="ex">What was thrown.</param>
    /// <param name="requestName">The command or query type.</param>
    /// <param name="requestId">This one execution.</param>
    /// <param name="correlationId">The request it belongs to.</param>
    /// <param name="requestShape">
    /// Names and sizes, never contents. A failure is exactly when someone wants
    /// the payload and exactly when writing it out is worst — the request that
    /// threw is the one carrying whatever was unusual about a person.
    /// </param>
    private void LogException(Exception ex, string requestName, string requestId,
        string correlationId, string requestShape)
    {
        var context = new Dictionary<string, object>
        {
            ["RequestName"] = requestName,
            ["RequestId"] = requestId,
            ["CorrelationId"] = correlationId,
            ["ExceptionType"] = ex.GetType().Name,
            ["RequestShape"] = requestShape
        };

        switch (ex)
        {
            case DomainException domainEx:
                // Don't log stack trace for business exceptions - just the message
                _logger.LogWarning(
                    "Business rule violation in {RequestName} [{RequestId}]: {ErrorCode} - {Message}",
                    requestName, requestId, domainEx.ErrorCode, domainEx.Message);
                break;

            case ValidationException:
                _logger.LogWarning(ex,
                    "Validation error in {RequestName} [{RequestId}]: {Message}",
                    requestName, requestId, ex.Message);
                break;

            case UnauthorizedAccessException:
                _logger.LogWarning(ex,
                    "Unauthorized access attempt in {RequestName} [{RequestId}] by user {UserId}",
                    requestName, requestId, GetUserId() ?? "Anonymous");
                break;

            case TaskCanceledException:
            case OperationCanceledException:
                _logger.LogInformation(
                    "Request {RequestName} [{RequestId}] was cancelled",
                    requestName, requestId);
                break;

            case TimeoutException:
                _logger.LogError(ex,
                    "Request {RequestName} [{RequestId}] timed out",
                    requestName, requestId);
                break;

            default:
                _logger.LogError(ex,
                    "Unhandled exception in {RequestName} [{RequestId}]: {Message}",
                    requestName, requestId, ex.Message);
                break;
        }
    }

    private LogLevel DetermineLogLevel(long elapsedMs, bool success)
    {
        if (!success)
            return LogLevel.Error;

        if (elapsedMs > CriticalRequestThresholdMs)
            return LogLevel.Warning;

        if (elapsedMs > SlowRequestThresholdMs)
            return LogLevel.Information;

        return LogLevel.Information;
    }

    private string GetCorrelationId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.Items.TryGetValue("CorrelationId", out var correlationId) == true)
        {
            return correlationId?.ToString() ?? Guid.NewGuid().ToString();
        }

        // Try to get from headers
        if (context?.Request.Headers.TryGetValue("X-Correlation-ID", out var headerValue) == true)
        {
            return headerValue.ToString();
        }

        // Try Activity.Current
        var activityCorrelationId = Activity.Current?.GetBaggageItem("CorrelationId");
        if (!string.IsNullOrEmpty(activityCorrelationId))
        {
            return activityCorrelationId;
        }

        return Guid.NewGuid().ToString();
    }

    private string? GetUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            return user.FindFirst("sub")?.Value 
                ?? user.FindFirst("UserId")?.Value 
                ?? user.Identity.Name;
        }

        return null;
    }
}
