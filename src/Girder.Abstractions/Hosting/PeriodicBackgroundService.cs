using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Abstractions.Hosting;

/// <summary>
/// A background service that does its work once at startup and then on a fixed
/// interval, until the host stops it.
/// </summary>
/// <remarks>
/// Derive rather than writing the loop again: the guarantees below are easy to
/// get subtly wrong, and each service that reimplements them is a place where
/// one of them can go missing.
/// <list type="bullet">
/// <item>The first run happens immediately, not after one interval.</item>
/// <item>A failing run is logged and the loop continues — one bad pass must not
/// silence the service until the process restarts.</item>
/// <item>Cancellation ends the loop promptly, including while waiting.</item>
/// </list>
/// <para>
/// The wait goes through <see cref="TimeProvider"/>, so the interval is
/// observable rather than merely elapsed: a test can move the clock and assert
/// that the next run happened, instead of sleeping and hoping.
/// </para>
/// </remarks>
public abstract class PeriodicBackgroundService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    protected PeriodicBackgroundService(ILogger logger, TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>How long to wait between runs.</summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>Name used in log messages. Defaults to the type name.</summary>
    protected virtual string ServiceName => GetType().Name;

    /// <summary>One pass of the work. Exceptions are logged, not propagated.</summary>
    protected abstract Task RunOnceAsync(CancellationToken cancellationToken);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{ServiceName} started", ServiceName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceName} failed a run and will try again", ServiceName);
            }

            try
            {
                await Task.Delay(Interval, _time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("{ServiceName} stopped", ServiceName);
    }
}
