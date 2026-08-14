// using Infrastructure.Resilience.System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Sockets;

namespace Infrastructure.Resilience
{
    /// <summary>
    /// Retry policy implementation with exponential backoff and jitter
    /// </summary>
    public class RetryPolicy : IRetryPolicy
    {
        private readonly RetryPolicyOptions _options;
        private readonly ILogger<RetryPolicy> _logger;
        private readonly string _name;
        private readonly Random _jitterRandom = new();

        // Statistics tracking
        private long _totalExecutions = 0;
        private long _successfulExecutions = 0;
        private long _failedExecutions = 0;
        private long _retryAttempts = 0;
        private readonly Dictionary<int, long> _retryDistribution = new();
        private readonly Dictionary<string, long> _exceptionTypes = new();
        private readonly object _statsLock = new object();

        public RetryPolicy(RetryPolicyOptions options, ILogger<RetryPolicy> logger, string name = "Default")
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _name = name;

            ValidateOptions();
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(operation, _options.ShouldRetry, cancellationToken);
        }

        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true; // Dummy return value
            }, cancellationToken);
        }

        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation, 
            Func<Exception, bool> shouldRetry, 
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(operation, (attempt, ex, delay) => 
            {
                _logger.LogWarning("Retry attempt {Attempt} for '{Name}' after {Delay}ms due to: {Exception}",
                    attempt, _name, delay.TotalMilliseconds, ex.Message);
            }, shouldRetry, cancellationToken);
        }

        public async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            Action<int, Exception, TimeSpan> onRetry,
            CancellationToken cancellationToken = default)
        {
            return await ExecuteAsync(operation, onRetry, _options.ShouldRetry, cancellationToken);
        }

        private async Task<T> ExecuteAsync<T>(
            Func<Task<T>> operation,
            Action<int, Exception, TimeSpan> onRetry,
            Func<Exception, bool> shouldRetry,
            CancellationToken cancellationToken = default)
        {
            IncrementTotalExecutions();

            var attempts = new List<RetryAttempt>();
            var stopwatch = Stopwatch.StartNew();

            for (int attempt = 0; attempt <= _options.MaxRetryAttempts; attempt++)
            {
                try
                {
                    // Execute the operation
                    var result = await operation();
                    
                    stopwatch.Stop();
                    RecordSuccess(attempts.Count);
                    
                    if (attempts.Any())
                    {
                        _logger.LogInformation("Operation '{Name}' succeeded after {Attempts} retry attempts in {Duration}ms",
                            _name, attempts.Count, stopwatch.ElapsedMilliseconds);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    // Record the attempt
                    var retryAttempt = new RetryAttempt
                    {
                        AttemptNumber = attempt,
                        Exception = ex,
                        Timestamp = DateTime.UtcNow
                    };

                    attempts.Add(retryAttempt);
                    RecordException(ex.GetType().Name);

                    // Check if we should retry
                    if (attempt >= _options.MaxRetryAttempts || !shouldRetry(ex) || cancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        RecordFailure(attempts.Count);
                        
                        _logger.LogError(ex, "Operation '{Name}' failed after {Attempts} attempts in {Duration}ms",
                            _name, attempts.Count, stopwatch.ElapsedMilliseconds);

                        throw new RetryPolicyException($"Operation '{_name}' failed after {attempts.Count} attempts", ex, attempts);
                    }

                    // Calculate delay for next retry
                    var delay = CalculateDelay(attempt);
                    retryAttempt.Delay = delay;

                    IncrementRetryAttempts();
                    onRetry(attempt + 1, ex, delay);

                    // Wait before retrying
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        stopwatch.Stop();
                        RecordFailure(attempts.Count);
                        throw;
                    }
                }
            }

            // This should never be reached due to the logic above, but just in case
            throw new InvalidOperationException("Unexpected end of retry loop");
        }

        public RetryPolicyStatistics GetStatistics()
        {
            lock (_statsLock)
            {
                return new RetryPolicyStatistics
                {
                    TotalExecutions = _totalExecutions,
                    SuccessfulExecutions = _successfulExecutions,
                    FailedExecutions = _failedExecutions,
                    RetryAttempts = _retryAttempts,
                    RetryDistribution = new Dictionary<int, long>(_retryDistribution),
                    ExceptionTypes = new Dictionary<string, long>(_exceptionTypes)
                };
            }
        }

        public void ResetStatistics()
        {
            lock (_statsLock)
            {
                _totalExecutions = 0;
                _successfulExecutions = 0;
                _failedExecutions = 0;
                _retryAttempts = 0;
                _retryDistribution.Clear();
                _exceptionTypes.Clear();
            }

            _logger.LogInformation("Reset statistics for retry policy '{Name}'", _name);
        }

        private TimeSpan CalculateDelay(int attempt)
        {
            var delay = _options.BackoffStrategy switch
            {
                BackoffStrategy.Linear => TimeSpan.FromMilliseconds(_options.BaseDelay.TotalMilliseconds * (attempt + 1)),
                BackoffStrategy.Exponential => TimeSpan.FromMilliseconds(_options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt)),
                BackoffStrategy.ExponentialWithJitter => CalculateExponentialWithJitter(attempt),
                BackoffStrategy.Fixed => _options.BaseDelay,
                _ => _options.BaseDelay
            };

            // Apply maximum delay cap
            if (delay > _options.MaxDelay)
            {
                delay = _options.MaxDelay;
            }

            return delay;
        }

        private TimeSpan CalculateExponentialWithJitter(int attempt)
        {
            var exponentialDelay = _options.BaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
            
            // Apply jitter: randomize ±25% of the calculated delay
            var jitterRange = exponentialDelay * 0.25;
            var jitter = (_jitterRandom.NextDouble() - 0.5) * 2 * jitterRange; // Range: -25% to +25%
            
            var finalDelay = exponentialDelay + jitter;
            
            // Ensure delay is not negative
            finalDelay = Math.Max(finalDelay, 0);
            
            return TimeSpan.FromMilliseconds(finalDelay);
        }

        private void IncrementTotalExecutions()
        {
            lock (_statsLock)
            {
                _totalExecutions++;
            }
        }

        private void IncrementRetryAttempts()
        {
            lock (_statsLock)
            {
                _retryAttempts++;
            }
        }

        private void RecordSuccess(int retryCount)
        {
            lock (_statsLock)
            {
                _successfulExecutions++;
                
                if (!_retryDistribution.ContainsKey(retryCount))
                {
                    _retryDistribution[retryCount] = 0;
                }
                _retryDistribution[retryCount]++;
            }
        }

        private void RecordFailure(int retryCount)
        {
            lock (_statsLock)
            {
                _failedExecutions++;
                
                if (!_retryDistribution.ContainsKey(retryCount))
                {
                    _retryDistribution[retryCount] = 0;
                }
                _retryDistribution[retryCount]++;
            }
        }

        private void RecordException(string exceptionType)
        {
            lock (_statsLock)
            {
                if (!_exceptionTypes.ContainsKey(exceptionType))
                {
                    _exceptionTypes[exceptionType] = 0;
                }
                _exceptionTypes[exceptionType]++;
            }
        }

        private void ValidateOptions()
        {
            if (_options.MaxRetryAttempts < 0)
            {
                throw new ArgumentException("MaxRetryAttempts must be non-negative");
            }

            if (_options.BaseDelay <= TimeSpan.Zero)
            {
                throw new ArgumentException("BaseDelay must be greater than zero");
            }

            if (_options.MaxDelay <= TimeSpan.Zero)
            {
                throw new ArgumentException("MaxDelay must be greater than zero");
            }

            if (_options.MaxDelay < _options.BaseDelay)
            {
                throw new ArgumentException("MaxDelay must be greater than or equal to BaseDelay");
            }
        }
    }

    /// <summary>
    /// Configuration options for retry policy
    /// </summary>
    public class RetryPolicyOptions
    {
        /// <summary>
        /// Maximum number of retry attempts
        /// </summary>
        public int MaxRetryAttempts { get; set; } = 3;

        /// <summary>
        /// Base delay between retry attempts
        /// </summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Maximum delay between retry attempts
        /// </summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Backoff strategy for calculating delays
        /// </summary>
        public BackoffStrategy BackoffStrategy { get; set; } = BackoffStrategy.ExponentialWithJitter;

        /// <summary>
        /// Predicate to determine if an exception should trigger a retry
        /// </summary>
        public Func<Exception, bool> ShouldRetry { get; set; } = DefaultShouldRetryPredicate;

        /// <summary>
        /// Default predicate for determining retry eligibility
        /// </summary>
        public static bool DefaultShouldRetryPredicate(Exception exception)
        {
            // Retry for network-related exceptions, timeouts, and transient failures
            return exception is HttpRequestException or
                   TaskCanceledException or
                   SocketException or
                   TimeoutException;
                //    (exception is SqlException sqlEx && IsTransientSqlException(sqlEx));
        }

        // private static bool IsTransientSqlException(SqlException ex)
        // {
        //     // Common transient SQL error codes
        //     var transientErrorCodes = new[] { 2, 20, 64, 233, 10053, 10054, 10060, 40197, 40501, 40613 };
        //     return transientErrorCodes.Contains(ex.Number);
        // }
    }

    /// <summary>
    /// Backoff strategy for retry delays
    /// </summary>
    public enum BackoffStrategy
    {
        /// <summary>
        /// Fixed delay between retries
        /// </summary>
        Fixed,

        /// <summary>
        /// Linear increase in delay (1x, 2x, 3x, etc.)
        /// </summary>
        Linear,

        /// <summary>
        /// Exponential increase in delay (1x, 2x, 4x, 8x, etc.)
        /// </summary>
        Exponential,

        /// <summary>
        /// Exponential increase with random jitter to avoid thundering herd
        /// </summary>
        ExponentialWithJitter
    }

    /// <summary>
    /// Exception thrown when retry policy is exhausted
    /// </summary>
    public class RetryPolicyException : Exception
    {
        public List<RetryAttempt> Attempts { get; }

        public RetryPolicyException(string message, Exception innerException, List<RetryAttempt> attempts)
            : base(message, innerException)
        {
            Attempts = attempts;
        }
    }
}
