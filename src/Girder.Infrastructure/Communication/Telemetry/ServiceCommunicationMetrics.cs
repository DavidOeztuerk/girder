using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Infrastructure.Communication.Telemetry;

/// <summary>
/// Implementation of service communication metrics
/// </summary>
public class ServiceCommunicationMetrics : IServiceCommunicationMetrics
{
    private readonly ILogger<ServiceCommunicationMetrics> _logger;
    private readonly object _lock = new();

    // Global metrics
    private long _totalRequests = 0;
    private long _successfulRequests = 0;
    private long _failedRequests = 0;
    private long _cachedRequests = 0;
    private long _retryAttempts = 0;
    private readonly List<double> _responseTimes = new();
    private DateTime _collectionStartedAt = DateTime.UtcNow;

    // Per-service metrics
    private readonly ConcurrentDictionary<string, ServiceMetricsData> _serviceMetrics = new();
    private readonly ConcurrentDictionary<int, long> _statusCodeDistribution = new();
    private readonly ConcurrentDictionary<string, long> _errorTypeDistribution = new();

    public ServiceCommunicationMetrics(ILogger<ServiceCommunicationMetrics> logger)
    {
        _logger = logger;
    }

    public void RecordServiceCall(string serviceName, string endpoint, string method, int statusCode, TimeSpan duration, bool fromCache = false)
    {
        lock (_lock)
        {
            _totalRequests++;
            _responseTimes.Add(duration.TotalMilliseconds);

            // Keep only recent response times (last 1000)
            if (_responseTimes.Count > 1000)
            {
                _responseTimes.RemoveAt(0);
            }

            if (statusCode >= 200 && statusCode < 300)
            {
                _successfulRequests++;
            }
            else
            {
                _failedRequests++;
            }

            if (fromCache)
            {
                _cachedRequests++;
            }

            // Status code distribution
            _statusCodeDistribution.AddOrUpdate(statusCode, 1, (_, count) => count + 1);
        }

        // Service-specific metrics
        var serviceData = _serviceMetrics.GetOrAdd(serviceName.ToLowerInvariant(), _ => new ServiceMetricsData
        {
            ServiceName = serviceName
        });

        serviceData.RecordRequest(endpoint, method, statusCode, duration, fromCache);

        _logger.LogDebug("Recorded service call: {Service} {Method} {Endpoint} - {StatusCode} in {Duration}ms (Cached: {FromCache})",
            serviceName, method, endpoint, statusCode, duration.TotalMilliseconds, fromCache);
    }

    public void RecordServiceCallFailure(string serviceName, string endpoint, string method, string errorType, TimeSpan duration)
    {
        lock (_lock)
        {
            _totalRequests++;
            _failedRequests++;
            _responseTimes.Add(duration.TotalMilliseconds);

            if (_responseTimes.Count > 1000)
            {
                _responseTimes.RemoveAt(0);
            }

            _errorTypeDistribution.AddOrUpdate(errorType, 1, (_, count) => count + 1);
        }

        var serviceData = _serviceMetrics.GetOrAdd(serviceName.ToLowerInvariant(), _ => new ServiceMetricsData
        {
            ServiceName = serviceName
        });

        serviceData.RecordFailure(endpoint, method, duration);

        _logger.LogWarning("Recorded service call failure: {Service} {Method} {Endpoint} - {ErrorType} after {Duration}ms",
            serviceName, method, endpoint, errorType, duration.TotalMilliseconds);
    }

    public void RecordRetryAttempt(string serviceName, int attemptNumber)
    {
        lock (_lock)
        {
            _retryAttempts++;
        }

        var serviceData = _serviceMetrics.GetOrAdd(serviceName.ToLowerInvariant(), _ => new ServiceMetricsData
        {
            ServiceName = serviceName
        });

        serviceData.RecordRetry();

        _logger.LogDebug("Recorded retry attempt: {Service} - Attempt {AttemptNumber}", serviceName, attemptNumber);
    }

    public void RecordCircuitBreakerStateChange(string serviceName, string state)
    {
        var serviceData = _serviceMetrics.GetOrAdd(serviceName.ToLowerInvariant(), _ => new ServiceMetricsData
        {
            ServiceName = serviceName
        });

        serviceData.CircuitBreakerState = state;

        _logger.LogInformation("Circuit breaker state changed for {Service}: {State}", serviceName, state);
    }

    public void RecordCacheOperation(string serviceName, string operation, bool success)
    {
        _logger.LogDebug("Recorded cache operation: {Service} - {Operation} (Success: {Success})",
            serviceName, operation, success);
    }

    public void RecordTokenOperation(string operation, bool success, TimeSpan? duration = null)
    {
        _logger.LogDebug("Recorded token operation: {Operation} (Success: {Success}) in {Duration}ms",
            operation, success, duration?.TotalMilliseconds ?? 0);
    }

    public ServiceCommunicationMetricsSummary GetMetricsSummary()
    {
        lock (_lock)
        {
            var summary = new ServiceCommunicationMetricsSummary
            {
                CollectionStartedAt = _collectionStartedAt,
                TotalRequests = _totalRequests,
                SuccessfulRequests = _successfulRequests,
                FailedRequests = _failedRequests,
                CachedRequests = _cachedRequests,
                RetryAttempts = _retryAttempts,
                AverageResponseTime = _responseTimes.Any() ? _responseTimes.Average() : 0,
                StatusCodeDistribution = new Dictionary<string, long>(
                    _statusCodeDistribution.Select(kvp => new KeyValuePair<string, long>(kvp.Key.ToString(), kvp.Value))),
                ErrorTypeDistribution = new Dictionary<string, long>(_errorTypeDistribution)
            };

            // Add service-specific metrics
            foreach (var kvp in _serviceMetrics)
            {
                summary.ServiceMetrics[kvp.Key] = kvp.Value.ToServiceMetrics();
            }

            return summary;
        }
    }

    public ServiceMetrics? GetServiceMetrics(string serviceName)
    {
        if (_serviceMetrics.TryGetValue(serviceName.ToLowerInvariant(), out var serviceData))
        {
            return serviceData.ToServiceMetrics();
        }

        return null;
    }

    public void ResetMetrics()
    {
        lock (_lock)
        {
            _totalRequests = 0;
            _successfulRequests = 0;
            _failedRequests = 0;
            _cachedRequests = 0;
            _retryAttempts = 0;
            _responseTimes.Clear();
            _collectionStartedAt = DateTime.UtcNow;
            _statusCodeDistribution.Clear();
            _errorTypeDistribution.Clear();
            _serviceMetrics.Clear();
        }

        _logger.LogInformation("Metrics reset");
    }

    private class ServiceMetricsData
    {
        private readonly object _lock = new();
        public string ServiceName { get; set; } = string.Empty;
        public long TotalRequests { get; private set; }
        public long SuccessfulRequests { get; private set; }
        public long FailedRequests { get; private set; }
        public long CachedRequests { get; private set; }
        public long RetryAttempts { get; private set; }
        public List<double> ResponseTimes { get; } = new();
        public string? CircuitBreakerState { get; set; }
        public DateTime? LastRequestAt { get; private set; }
        public ConcurrentDictionary<string, EndpointMetricsData> Endpoints { get; } = new();

        public void RecordRequest(string endpoint, string method, int statusCode, TimeSpan duration, bool fromCache)
        {
            lock (_lock)
            {
                TotalRequests++;
                ResponseTimes.Add(duration.TotalMilliseconds);
                LastRequestAt = DateTime.UtcNow;

                if (ResponseTimes.Count > 1000)
                {
                    ResponseTimes.RemoveAt(0);
                }

                if (statusCode >= 200 && statusCode < 300)
                {
                    SuccessfulRequests++;
                }
                else
                {
                    FailedRequests++;
                }

                if (fromCache)
                {
                    CachedRequests++;
                }
            }

            var endpointKey = $"{method}:{endpoint}";
            var endpointData = Endpoints.GetOrAdd(endpointKey, _ => new EndpointMetricsData
            {
                Endpoint = endpoint,
                Method = method
            });

            endpointData.RecordRequest(statusCode, duration, fromCache);
        }

        public void RecordFailure(string endpoint, string method, TimeSpan duration)
        {
            lock (_lock)
            {
                TotalRequests++;
                FailedRequests++;
                ResponseTimes.Add(duration.TotalMilliseconds);
                LastRequestAt = DateTime.UtcNow;

                if (ResponseTimes.Count > 1000)
                {
                    ResponseTimes.RemoveAt(0);
                }
            }

            var endpointKey = $"{method}:{endpoint}";
            var endpointData = Endpoints.GetOrAdd(endpointKey, _ => new EndpointMetricsData
            {
                Endpoint = endpoint,
                Method = method
            });

            endpointData.RecordFailure(duration);
        }

        public void RecordRetry()
        {
            lock (_lock)
            {
                RetryAttempts++;
            }
        }

        public ServiceMetrics ToServiceMetrics()
        {
            lock (_lock)
            {
                var sortedTimes = ResponseTimes.OrderBy(t => t).ToList();

                return new ServiceMetrics
                {
                    ServiceName = ServiceName,
                    TotalRequests = TotalRequests,
                    SuccessfulRequests = SuccessfulRequests,
                    FailedRequests = FailedRequests,
                    CachedRequests = CachedRequests,
                    RetryAttempts = RetryAttempts,
                    AverageResponseTime = sortedTimes.Any() ? sortedTimes.Average() : 0,
                    P50ResponseTime = sortedTimes.Any() ? Percentile(sortedTimes, 0.50) : 0,
                    P95ResponseTime = sortedTimes.Any() ? Percentile(sortedTimes, 0.95) : 0,
                    P99ResponseTime = sortedTimes.Any() ? Percentile(sortedTimes, 0.99) : 0,
                    CircuitBreakerState = CircuitBreakerState,
                    LastRequestAt = LastRequestAt,
                    EndpointMetrics = Endpoints.ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.ToEndpointMetrics())
                };
            }
        }

        private static double Percentile(List<double> sequence, double percentile)
        {
            int n = sequence.Count;
            double index = (n - 1) * percentile;
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);

            if (lower == upper)
            {
                return sequence[lower];
            }

            double fraction = index - lower;
            return sequence[lower] + fraction * (sequence[upper] - sequence[lower]);
        }
    }

    private class EndpointMetricsData
    {
        private readonly object _lock = new();
        public string Endpoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public long TotalRequests { get; private set; }
        public long SuccessfulRequests { get; private set; }
        public long FailedRequests { get; private set; }
        public long CachedRequests { get; private set; }
        public List<double> ResponseTimes { get; } = new();
        public ConcurrentDictionary<int, long> StatusCodeCounts { get; } = new();

        public void RecordRequest(int statusCode, TimeSpan duration, bool fromCache)
        {
            lock (_lock)
            {
                TotalRequests++;
                ResponseTimes.Add(duration.TotalMilliseconds);

                if (ResponseTimes.Count > 100)
                {
                    ResponseTimes.RemoveAt(0);
                }

                if (statusCode >= 200 && statusCode < 300)
                {
                    SuccessfulRequests++;
                }
                else
                {
                    FailedRequests++;
                }

                if (fromCache)
                {
                    CachedRequests++;
                }

                StatusCodeCounts.AddOrUpdate(statusCode, 1, (_, count) => count + 1);
            }
        }

        public void RecordFailure(TimeSpan duration)
        {
            lock (_lock)
            {
                TotalRequests++;
                FailedRequests++;
                ResponseTimes.Add(duration.TotalMilliseconds);

                if (ResponseTimes.Count > 100)
                {
                    ResponseTimes.RemoveAt(0);
                }
            }
        }

        public EndpointMetrics ToEndpointMetrics()
        {
            lock (_lock)
            {
                return new EndpointMetrics
                {
                    Endpoint = Endpoint,
                    Method = Method,
                    TotalRequests = TotalRequests,
                    SuccessfulRequests = SuccessfulRequests,
                    FailedRequests = FailedRequests,
                    CachedRequests = CachedRequests,
                    AverageResponseTime = ResponseTimes.Any() ? ResponseTimes.Average() : 0,
                    StatusCodeCounts = new Dictionary<int, long>(StatusCodeCounts)
                };
            }
        }
    }
}
