using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CQRS.Behaviors;

/// <summary>
/// Performance monitoring behavior
/// </summary>
/// <typeparam name="TRequest"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger) 
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<PerformanceBehavior<TRequest, TResponse>> _logger = logger;
    private const int SlowRequestThresholdMs = 1000;

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await next(cancellationToken);
        stopwatch.Stop();

        var elapsedMs = stopwatch.ElapsedMilliseconds;
        var requestName = typeof(TRequest).Name;

        if (elapsedMs > SlowRequestThresholdMs)
        {
            _logger.LogWarning("Slow request detected: {RequestName} took {ElapsedMs}ms",
                requestName, elapsedMs);
        }
        else
        {
            _logger.LogDebug("Request {RequestName} completed in {ElapsedMs}ms",
                requestName, elapsedMs);
        }

        return response;
    }
}
