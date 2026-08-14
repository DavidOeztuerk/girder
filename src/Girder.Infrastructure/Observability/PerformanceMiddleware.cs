using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;

namespace Girder.Infrastructure.Observability;

/// <summary>
/// Middleware for collecting performance metrics
/// </summary>
public class PerformanceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IPerformanceMetrics _performanceMetrics;
    private readonly ILogger<PerformanceMiddleware> _logger;

    public PerformanceMiddleware(
        RequestDelegate next,
        IPerformanceMetrics performanceMetrics,
        ILogger<PerformanceMiddleware> logger)
    {
        _next = next;
        _performanceMetrics = performanceMetrics;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = context.Request.Method;
        var endpoint = GetNormalizedPath(context.Request.Path);

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var duration = stopwatch.Elapsed.TotalMilliseconds;

            // Record performance metrics
            _performanceMetrics.RecordRequest(method, endpoint, statusCode, duration);

            // Log slow requests
            if (duration > 1000) // More than 1 second
            {
                _logger.LogWarning(
                    "Slow request detected: {Method} {Path} took {Duration}ms (Status: {StatusCode})",
                    method, context.Request.Path, duration, statusCode);
            }
        }
    }

    internal static string GetNormalizedPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Normalize path by replacing IDs with placeholders
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>();

        foreach (var segment in segments)
        {
            if (IsIdSegment(segment))
            {
                normalizedSegments.Add("{id}");
            }
            else
            {
                normalizedSegments.Add(segment.ToLowerInvariant());
            }
        }

        return "/" + string.Join("/", normalizedSegments);
    }

    internal static bool IsIdSegment(string segment)
    {
        // Check for GUID
        if (Guid.TryParse(segment, out _))
            return true;

        // Check for numeric ID
        if (long.TryParse(segment, out _))
            return true;

        // Check for common ID patterns
        if (segment.Length > 10 && segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            return true;

        return false;
    }
}

/// <summary>
/// Performance monitoring service for tracking application metrics
/// </summary>
public class PerformanceMonitoringService : IPerformanceMonitoringService
{
    private readonly IPerformanceMetrics _metrics;
    private readonly ILogger<PerformanceMonitoringService> _logger;
    private readonly Timer _monitoringTimer;
    private readonly List<PerformanceAlert> _alerts = new();

    public PerformanceMonitoringService(
        IPerformanceMetrics metrics,
        ILogger<PerformanceMonitoringService> logger)
    {
        _metrics = metrics;
        _logger = logger;

        // Set up periodic monitoring
        _monitoringTimer = new Timer(MonitorPerformance, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        
        RegisterDefaultAlerts();
    }

    public ITimerScope StartTimer(string operationName)
    {
        return new TimerScope(operationName, _metrics, _logger);
    }

    public void RecordCustomMetric(string name, double value, params KeyValuePair<string, object?>[] tags)
    {
        // This would be implemented based on the specific metric type
        _logger.LogDebug("Custom metric {Name}: {Value}", name, value);
    }

    public void AddPerformanceAlert(PerformanceAlert alert)
    {
        _alerts.Add(alert);
        _logger.LogInformation("Added performance alert: {AlertName}", alert.Name);
    }

    public PerformanceSnapshot GetCurrentSnapshot()
    {
        var process = Process.GetCurrentProcess();
        
        return new PerformanceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            CpuUsagePercent = GetCpuUsage(),
            MemoryUsageMB = process.WorkingSet64 / (1024.0 * 1024.0),
            ThreadCount = process.Threads.Count,
            GcTotalMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            UptimeSeconds = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds
        };
    }

    private void MonitorPerformance(object? state)
    {
        try
        {
            var snapshot = GetCurrentSnapshot();
            
            // Check alerts
            foreach (var alert in _alerts)
            {
                if (alert.ShouldTrigger(snapshot))
                {
                    _logger.LogWarning("Performance alert triggered: {AlertName} - {Description}", 
                        alert.Name, alert.Description);
                }
            }

            // Log periodic performance info
            _logger.LogDebug("Performance snapshot: CPU {CPU:F1}%, Memory {Memory:F1}MB, Threads {Threads}",
                snapshot.CpuUsagePercent, snapshot.MemoryUsageMB, snapshot.ThreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during performance monitoring");
        }
    }

    private void RegisterDefaultAlerts()
    {
        // High CPU usage alert
        AddPerformanceAlert(new PerformanceAlert
        {
            Name = "HighCpuUsage",
            Description = "CPU usage is above 80%",
            Condition = snapshot => snapshot.CpuUsagePercent > 80
        });

        // High memory usage alert
        AddPerformanceAlert(new PerformanceAlert
        {
            Name = "HighMemoryUsage",
            Description = "Memory usage is above 1GB",
            Condition = snapshot => snapshot.MemoryUsageMB > 1024
        });

        // High thread count alert
        AddPerformanceAlert(new PerformanceAlert
        {
            Name = "HighThreadCount",
            Description = "Thread count is above 500",
            Condition = snapshot => snapshot.ThreadCount > 500
        });

        // Frequent GC Gen2 collections alert
        AddPerformanceAlert(new PerformanceAlert
        {
            Name = "FrequentGcGen2",
            Description = "High number of Gen2 GC collections",
            Condition = snapshot => snapshot.Gen2Collections > 100
        });
    }

    private static double GetCpuUsage()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(100); // Wait a bit to measure CPU usage

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

    public void Dispose()
    {
        _monitoringTimer?.Dispose();
    }
}

/// <summary>
/// Interface for performance monitoring service
/// </summary>
public interface IPerformanceMonitoringService : IDisposable
{
    /// <summary>
    /// Start a timer for measuring operation duration
    /// </summary>
    ITimerScope StartTimer(string operationName);

    /// <summary>
    /// Record a custom metric
    /// </summary>
    void RecordCustomMetric(string name, double value, params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Add a performance alert
    /// </summary>
    void AddPerformanceAlert(PerformanceAlert alert);

    /// <summary>
    /// Get current performance snapshot
    /// </summary>
    PerformanceSnapshot GetCurrentSnapshot();
}

/// <summary>
/// Timer scope for measuring operation duration
/// </summary>
public interface ITimerScope : IDisposable
{
    /// <summary>
    /// Add tags to the timer
    /// </summary>
    void AddTag(string key, object? value);
}

/// <summary>
/// Implementation of timer scope
/// </summary>
public class TimerScope : ITimerScope
{
    private readonly string _operationName;
    private readonly IPerformanceMetrics _metrics;
    private readonly ILogger _logger;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, object?> _tags = new();

    public TimerScope(string operationName, IPerformanceMetrics metrics, ILogger logger)
    {
        _operationName = operationName;
        _metrics = metrics;
        _logger = logger;
        _stopwatch = Stopwatch.StartNew();
    }

    public void AddTag(string key, object? value)
    {
        _tags[key] = value;
    }

    public void Dispose()
    {
        _stopwatch.Stop();
        var duration = _stopwatch.Elapsed.TotalMilliseconds;

        // Record metric based on operation name
        if (_operationName.Contains("database", StringComparison.OrdinalIgnoreCase))
        {
            _metrics.RecordDatabaseQuery(_operationName, duration);
        }
        else if (_operationName.Contains("cache", StringComparison.OrdinalIgnoreCase))
        {
            var hit = _tags.ContainsKey("hit") && (bool)(_tags["hit"] ?? false);
            _metrics.RecordCacheOperation(_operationName, hit, duration);
        }

        // Log slow operations
        if (duration > 1000)
        {
            _logger.LogWarning("Slow operation: {Operation} took {Duration}ms", _operationName, duration);
        }
    }
}

/// <summary>
/// Performance alert configuration
/// </summary>
public class PerformanceAlert
{
    /// <summary>
    /// Alert name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Alert description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Condition to trigger the alert
    /// </summary>
    public Func<PerformanceSnapshot, bool> Condition { get; set; } = _ => false;

    /// <summary>
    /// Check if the alert should trigger
    /// </summary>
    public bool ShouldTrigger(PerformanceSnapshot snapshot)
    {
        return Condition(snapshot);
    }
}

/// <summary>
/// Performance snapshot data
/// </summary>
public class PerformanceSnapshot
{
    public DateTime Timestamp { get; set; }
    public double CpuUsagePercent { get; set; }
    public double MemoryUsageMB { get; set; }
    public int ThreadCount { get; set; }
    public double GcTotalMemoryMB { get; set; }
    public int Gen0Collections { get; set; }
    public int Gen1Collections { get; set; }
    public int Gen2Collections { get; set; }
    public double UptimeSeconds { get; set; }
}

public static class PerformanceMiddlewareExtensions
{
    public static IApplicationBuilder UsePerformanceMonitoring(this IApplicationBuilder app)
    {
        return app.UseMiddleware<PerformanceMiddleware>();
    }
}