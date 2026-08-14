using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Girder.Infrastructure.Resilience;

/// <summary>
/// Circuit breaker implementation for resilience patterns
/// </summary>
public class CircuitBreaker : ICircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<CircuitBreaker> _logger;
    private readonly string _name;

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _consecutiveFailures = 0;
    private DateTime _lastFailureTime = DateTime.MinValue;
    private DateTime _lastOpenTime = DateTime.MinValue;
    private long _successCount = 0;
    private long _failureCount = 0;
    private long _tripCount = 0;
    private string? _lastError;
    private readonly List<double> _responseTimes = new();
    private readonly object _lock = new object();

    public CircuitBreaker(CircuitBreakerOptions options, ILogger<CircuitBreaker> logger, string name = "Default")
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _name = name;

        ValidateOptions();
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(operation, () => throw new CircuitBreakerOpenException($"Circuit breaker '{_name}' is open"), cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>> fallback, CancellationToken cancellationToken = default)
    {
        if (!CanExecute())
        {
            _logger.LogWarning("Circuit breaker '{Name}' is open, executing fallback", _name);
            return await fallback();
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = new CancellationTokenSource(_options.Timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var result = await operation.Invoke();
            
            stopwatch.Stop();
            RecordSuccess(stopwatch.Elapsed);
            
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            RecordFailure(stopwatch.Elapsed, "Operation was cancelled");
            throw;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            RecordFailure(stopwatch.Elapsed, "Operation timed out");
            throw new TimeoutException($"Operation timed out after {_options.Timeout.TotalMilliseconds}ms");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordFailure(stopwatch.Elapsed, ex.Message);

            // HalfOpen + Failure → reopen circuit (service hasn't recovered)
            bool reopenedFromHalfOpen;
            lock (_lock)
            {
                reopenedFromHalfOpen = _state == CircuitBreakerState.HalfOpen;
                if (reopenedFromHalfOpen)
                {
                    _state = CircuitBreakerState.Open;
                    _lastOpenTime = DateTime.UtcNow;
                    _tripCount++;
                    _logger.LogWarning(ex, "Circuit breaker '{Name}' re-opened after half-open failure", _name);
                }
            }

            // Closed + enough failures → trip to Open (skip if already reopened from HalfOpen)
            if (!reopenedFromHalfOpen && ShouldTripCircuit())
            {
                TripCircuit();
            }

            // If circuit is now open, try fallback
            if (_state == CircuitBreakerState.Open)
            {
                _logger.LogWarning(ex, "Circuit breaker '{Name}' tripped, executing fallback", _name);
                return await fallback();
            }

            throw;
        }
    }

    public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(operation, () => throw new CircuitBreakerOpenException($"Circuit breaker '{_name}' is open"), cancellationToken);
    }

    public async Task ExecuteAsync(Func<Task> operation, Func<Task> fallback, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true; // Dummy return value
        }, async () =>
        {
            await fallback();
            return true; // Dummy return value
        }, cancellationToken);
    }

    public CircuitBreakerStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new CircuitBreakerStatistics
            {
                State = _state,
                SuccessCount = _successCount,
                FailureCount = _failureCount,
                TripCount = _tripCount,
                LastOpenedAt = _lastOpenTime == DateTime.MinValue ? null : _lastOpenTime,
                LastClosedAt = _lastFailureTime == DateTime.MinValue ? null : _lastFailureTime,
                ConsecutiveFailures = _consecutiveFailures,
                AverageResponseTime = _responseTimes.Any() ? _responseTimes.Average() : 0,
                LastError = _lastError
            };
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _consecutiveFailures = 0;
            _lastError = null;
            
            _logger.LogInformation("Circuit breaker '{Name}' has been reset", _name);
        }
    }

    public void ForceOpen()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Open;
            _lastOpenTime = DateTime.UtcNow;
            _tripCount++;
            
            _logger.LogWarning("Circuit breaker '{Name}' has been forced open", _name);
        }
    }

    private bool CanExecute()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return true;

                case CircuitBreakerState.Open:
                    if (DateTime.UtcNow - _lastOpenTime >= _options.DurationOfBreak)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _logger.LogInformation("Circuit breaker '{Name}' transitioning to half-open", _name);
                        return true;
                    }
                    return false;

                case CircuitBreakerState.HalfOpen:
                    return true;

                default:
                    return false;
            }
        }
    }

    private void RecordSuccess(TimeSpan responseTime)
    {
        lock (_lock)
        {
            _successCount++;
            _consecutiveFailures = 0;
            _responseTimes.Add(responseTime.TotalMilliseconds);

            // Keep only recent response times for average calculation
            if (_responseTimes.Count > 100)
            {
                _responseTimes.RemoveAt(0);
            }

            if (_state == CircuitBreakerState.HalfOpen)
            {
                CloseCircuit();
            }

            _logger.LogDebug("Circuit breaker '{Name}' recorded success in {ResponseTime}ms", _name, responseTime.TotalMilliseconds);
        }
    }

    private void RecordFailure(TimeSpan responseTime, string error)
    {
        lock (_lock)
        {
            _failureCount++;
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;
            _lastError = error;
            _responseTimes.Add(responseTime.TotalMilliseconds);

            // Keep only recent response times for average calculation
            if (_responseTimes.Count > 100)
            {
                _responseTimes.RemoveAt(0);
            }

            _logger.LogWarning("Circuit breaker '{Name}' recorded failure in {ResponseTime}ms: {Error}", 
                _name, responseTime.TotalMilliseconds, error);
        }
    }

    private bool ShouldTripCircuit()
    {
        lock (_lock)
        {
            if (_state != CircuitBreakerState.Closed)
            {
                return false;
            }

            // Check failure threshold
            if (_consecutiveFailures >= _options.ExceptionsAllowedBeforeBreaking)
            {
                return true;
            }

            // Check failure rate (if we have enough samples)
            var totalRequests = _successCount + _failureCount;
            if (totalRequests >= _options.MinimumThroughput)
            {
                var failureRate = (double)_failureCount / totalRequests;
                if (failureRate >= _options.FailureThreshold)
                {
                    return true;
                }
            }

            return false;
        }
    }

    private void TripCircuit()
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.Closed)
            {
                _state = CircuitBreakerState.Open;
                _lastOpenTime = DateTime.UtcNow;
                _tripCount++;

                _logger.LogWarning("Circuit breaker '{Name}' has been tripped after {ConsecutiveFailures} consecutive failures",
                    _name, _consecutiveFailures);
            }
        }
    }

    private void CloseCircuit()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _consecutiveFailures = 0;

            _logger.LogInformation("Circuit breaker '{Name}' has been closed", _name);
        }
    }

    private void ValidateOptions()
    {
        if (_options.ExceptionsAllowedBeforeBreaking <= 0)
        {
            throw new ArgumentException("ExceptionsAllowedBeforeBreaking must be greater than 0");
        }

        if (_options.DurationOfBreak <= TimeSpan.Zero)
        {
            throw new ArgumentException("DurationOfBreak must be greater than zero");
        }

        if (_options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be greater than zero");
        }

        if (_options.FailureThreshold < 0 || _options.FailureThreshold > 1)
        {
            throw new ArgumentException("FailureThreshold must be between 0 and 1");
        }
    }
}

/// <summary>
/// Configuration options for circuit breaker
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>
    /// Number of consecutive exceptions before breaking the circuit
    /// </summary>
    public int ExceptionsAllowedBeforeBreaking { get; set; } = 5;

    /// <summary>
    /// Duration to keep the circuit open before attempting to close it
    /// </summary>
    public TimeSpan DurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Timeout for individual operations
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Failure threshold (0.0 to 1.0) for tripping based on failure rate
    /// </summary>
    public double FailureThreshold { get; set; } = 0.5;

    /// <summary>
    /// Minimum number of requests before failure rate is considered
    /// </summary>
    public int MinimumThroughput { get; set; } = 10;
}

/// <summary>
/// Exception thrown when circuit breaker is open
/// </summary>
public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}