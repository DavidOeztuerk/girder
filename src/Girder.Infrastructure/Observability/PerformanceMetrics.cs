using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Infrastructure.Observability;

/// <summary>
/// Performance metrics collector for the Skillswap application
/// </summary>
public class PerformanceMetrics : IPerformanceMetrics
{
    private static readonly Meter Meter = new(TelemetryConstants.MeterName);
    private readonly object _requestSnapshotLock = new();
    private DateOnly _requestSnapshotDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private long _totalRequestsToday;
    private long _totalErrorsToday;
    private double _totalRequestDurationMs;

    // Request metrics
    private static readonly Counter<long> RequestCount = Meter.CreateCounter<long>(
        "skillswap.requests.total",
        "requests",
        "Total number of HTTP requests");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "skillswap.requests.duration",
        "milliseconds",
        "HTTP request duration");

    private static readonly Counter<long> RequestErrors = Meter.CreateCounter<long>(
        "skillswap.requests.errors",
        "errors",
        "Number of HTTP request errors");

    // Database metrics
    private static readonly Counter<long> DatabaseQueries = Meter.CreateCounter<long>(
        "skillswap.database.queries.total",
        "queries",
        "Total number of database queries");

    private static readonly Histogram<double> DatabaseQueryDuration = Meter.CreateHistogram<double>(
        "skillswap.database.queries.duration",
        "milliseconds",
        "Database query duration");

    private static readonly Counter<long> DatabaseErrors = Meter.CreateCounter<long>(
        "skillswap.database.errors",
        "errors",
        "Number of database errors");

    // Cache metrics
    private static readonly Counter<long> CacheOperations = Meter.CreateCounter<long>(
        "skillswap.cache.operations.total",
        "operations",
        "Total number of cache operations");

    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "skillswap.cache.hits",
        "hits",
        "Number of cache hits");

    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "skillswap.cache.misses",
        "misses",
        "Number of cache misses");

    private static readonly Histogram<double> CacheOperationDuration = Meter.CreateHistogram<double>(
        "skillswap.cache.operations.duration",
        "milliseconds",
        "Cache operation duration");

    // Business metrics
    private static readonly Counter<long> UserActions = Meter.CreateCounter<long>(
        "skillswap.users.actions",
        "actions",
        "User actions performed");

    private static readonly Counter<long> SkillsManaged = Meter.CreateCounter<long>(
        "skillswap.skills.managed",
        "skills",
        "Skills created, updated, or deleted");

    private static readonly Counter<long> MatchesProcessed = Meter.CreateCounter<long>(
        "skillswap.matches.processed",
        "matches",
        "Matches created, accepted, or rejected");

    // System metrics
    private static readonly ObservableGauge<double> CpuUsage = Meter.CreateObservableGauge<double>(
        "skillswap.system.cpu.usage",
        () => GetCpuUsage(),
        "percent",
        "CPU usage percentage");

    private static readonly ObservableGauge<long> MemoryUsage = Meter.CreateObservableGauge<long>(
        "skillswap.system.memory.usage",
        () => GC.GetTotalMemory(false),
        "bytes",
        "Memory usage in bytes");

    private static readonly ObservableGauge<int> ThreadCount = Meter.CreateObservableGauge<int>(
        "skillswap.system.threads.count",
        () => Process.GetCurrentProcess().Threads.Count,
        "threads",
        "Number of threads");

    private static readonly ObservableGauge<long> WorkingSet = Meter.CreateObservableGauge<long>(
        "skillswap.system.workingset",
        () => Process.GetCurrentProcess().WorkingSet64,
        "bytes",
        "Working set size in bytes");

    // Rate limiting metrics
    private static readonly Counter<long> RateLimitExceeded = Meter.CreateCounter<long>(
        "skillswap.ratelimit.exceeded",
        "requests",
        "Number of rate limited requests");

    private static readonly Histogram<double> RateLimitCheckDuration = Meter.CreateHistogram<double>(
        "skillswap.ratelimit.check.duration",
        "milliseconds",
        "Rate limit check duration");

    // Security middleware metrics
    private static readonly Counter<long> SecurityMiddlewareExecutions = Meter.CreateCounter<long>(
        "skillswap.security.middleware.executions",
        "executions",
        "Number of security middleware executions");

    private static readonly Histogram<double> SecurityMiddlewareDuration = Meter.CreateHistogram<double>(
        "skillswap.security.middleware.duration",
        "milliseconds",
        "Security middleware execution duration");

    // Circuit breaker metrics
    private static readonly Counter<long> CircuitBreakerStateChanges = Meter.CreateCounter<long>(
        "skillswap.circuitbreaker.state.changes",
        "changes",
        "Circuit breaker state changes");

    private static readonly Counter<long> CircuitBreakerOperations = Meter.CreateCounter<long>(
        "skillswap.circuitbreaker.operations",
        "operations",
        "Operations through circuit breaker");

    public void RecordRequest(string method, string endpoint, int statusCode, double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("method", method),
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status_code", statusCode)
        };

        RequestCount.Add(1, tags);
        RequestDuration.Record(durationMs, tags);

        if (statusCode >= 400)
        {
            RequestErrors.Add(1, tags);
        }

        RecordRequestSnapshot(statusCode, durationMs, DateTime.UtcNow);
    }

    public RequestMetricsSnapshot GetRequestMetricsSnapshot(DateTime utcNow)
    {
        lock (_requestSnapshotLock)
        {
            ResetRequestSnapshotIfNeeded(utcNow);

            var averageResponseTimeMs = _totalRequestsToday > 0
                ? Math.Round(_totalRequestDurationMs / _totalRequestsToday, 1)
                : 0;
            var errorRatePercent = _totalRequestsToday > 0
                ? Math.Round((double)_totalErrorsToday / _totalRequestsToday * 100, 1)
                : 0;

            return new RequestMetricsSnapshot(
                _totalRequestsToday,
                _totalErrorsToday,
                errorRatePercent,
                averageResponseTimeMs);
        }
    }

    private void RecordRequestSnapshot(int statusCode, double durationMs, DateTime utcNow)
    {
        lock (_requestSnapshotLock)
        {
            ResetRequestSnapshotIfNeeded(utcNow);

            _totalRequestsToday++;
            _totalRequestDurationMs += durationMs;

            if (statusCode >= 400)
            {
                _totalErrorsToday++;
            }
        }
    }

    private void ResetRequestSnapshotIfNeeded(DateTime utcNow)
    {
        var currentDay = DateOnly.FromDateTime(utcNow);
        if (currentDay == _requestSnapshotDay)
        {
            return;
        }

        _requestSnapshotDay = currentDay;
        _totalRequestsToday = 0;
        _totalErrorsToday = 0;
        _totalRequestDurationMs = 0;
    }

    public void RecordDatabaseQuery(string operation, double durationMs, bool success = true)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("success", success)
        };

        DatabaseQueries.Add(1, tags);
        DatabaseQueryDuration.Record(durationMs, tags);

        if (!success)
        {
            DatabaseErrors.Add(1, tags);
        }
    }

    public void RecordCacheOperation(string operation, bool hit, double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("operation", operation)
        };

        CacheOperations.Add(1, tags);
        CacheOperationDuration.Record(durationMs, tags);

        if (hit)
        {
            CacheHits.Add(1, tags);
        }
        else
        {
            CacheMisses.Add(1, tags);
        }
    }

    public void RecordUserAction(string action, string userId)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("user_id", userId)
        };

        UserActions.Add(1, tags);
    }

    public void RecordSkillManagement(string operation, string category)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("category", category)
        };

        SkillsManaged.Add(1, tags);
    }

    public void RecordMatchProcessing(string operation, string matchType)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("match_type", matchType)
        };

        MatchesProcessed.Add(1, tags);
    }

    public void RecordRateLimitExceeded(string clientType, string endpoint, double checkDurationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("client_type", clientType),
            new KeyValuePair<string, object?>("endpoint", endpoint)
        };

        RateLimitExceeded.Add(1, tags);
        RateLimitCheckDuration.Record(checkDurationMs, tags);
    }

    public void RecordSecurityMiddlewarePerformance(string middlewareName, string endpoint, double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("middleware_name", middlewareName),
            new KeyValuePair<string, object?>("endpoint", endpoint)
        };

        SecurityMiddlewareExecutions.Add(1, tags);
        SecurityMiddlewareDuration.Record(durationMs, tags);
    }

    public void RecordCircuitBreakerStateChange(string circuitBreakerName, string fromState, string toState)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("circuit_breaker", circuitBreakerName),
            new KeyValuePair<string, object?>("from_state", fromState),
            new KeyValuePair<string, object?>("to_state", toState)
        };

        CircuitBreakerStateChanges.Add(1, tags);
    }

    public void RecordCircuitBreakerOperation(string circuitBreakerName, bool success, double durationMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("circuit_breaker", circuitBreakerName),
            new KeyValuePair<string, object?>("success", success)
        };

        CircuitBreakerOperations.Add(1, tags);
    }

    private static double GetCpuUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(500); // Wait a bit to measure CPU usage

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return cpuUsageTotal * 100;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// Interface for performance metrics
/// </summary>
public interface IPerformanceMetrics
{
    /// <summary>
    /// Record HTTP request metrics
    /// </summary>
    void RecordRequest(string method, string endpoint, int statusCode, double durationMs);

    /// <summary>
    /// Get in-process HTTP request metrics for the current UTC day.
    /// </summary>
    RequestMetricsSnapshot GetRequestMetricsSnapshot(DateTime utcNow);

    /// <summary>
    /// Record database query metrics
    /// </summary>
    void RecordDatabaseQuery(string operation, double durationMs, bool success = true);

    /// <summary>
    /// Record cache operation metrics
    /// </summary>
    void RecordCacheOperation(string operation, bool hit, double durationMs);

    /// <summary>
    /// Record user action metrics
    /// </summary>
    void RecordUserAction(string action, string userId);

    /// <summary>
    /// Record skill management metrics
    /// </summary>
    void RecordSkillManagement(string operation, string category);

    /// <summary>
    /// Record match processing metrics
    /// </summary>
    void RecordMatchProcessing(string operation, string matchType);

    /// <summary>
    /// Record rate limiting metrics
    /// </summary>
    void RecordRateLimitExceeded(string clientType, string endpoint, double checkDurationMs);

    /// <summary>
    /// Record security middleware execution metrics
    /// </summary>
    void RecordSecurityMiddlewarePerformance(string middlewareName, string endpoint, double durationMs);

    /// <summary>
    /// Record circuit breaker state change
    /// </summary>
    void RecordCircuitBreakerStateChange(string circuitBreakerName, string fromState, string toState);

    /// <summary>
    /// Record circuit breaker operation
    /// </summary>
    void RecordCircuitBreakerOperation(string circuitBreakerName, bool success, double durationMs);
}

public readonly record struct RequestMetricsSnapshot(
    long TotalRequestsToday,
    long TotalErrorsToday,
    double ErrorRatePercent,
    double AverageResponseTimeMs);
