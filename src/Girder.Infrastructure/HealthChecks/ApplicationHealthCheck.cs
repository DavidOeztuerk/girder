using Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime;

namespace Infrastructure.HealthChecks;

/// <summary>
/// Health check for overall application health and performance
/// </summary>
public class ApplicationHealthCheck : IHealthCheck
{
    private readonly ILogger<ApplicationHealthCheck> _logger;

    public ApplicationHealthCheck(ILogger<ApplicationHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var startTime = currentProcess.StartTime;
            var uptime = DateTime.UtcNow - startTime;

            var data = new Dictionary<string, object>
            {
                ["uptime_seconds"] = uptime.TotalSeconds,
                ["uptime_formatted"] = FormatUptime(uptime),
                ["process_id"] = currentProcess.Id,
                ["process_name"] = currentProcess.ProcessName,
                ["machine_name"] = Environment.MachineName,
                ["processor_count"] = Environment.ProcessorCount,
                ["framework_version"] = Environment.Version.ToString(),
                ["gc_total_memory"] = GC.GetTotalMemory(false),
                ["gc_gen0_collections"] = GC.CollectionCount(0),
                ["gc_gen1_collections"] = GC.CollectionCount(1),
                ["gc_gen2_collections"] = GC.CollectionCount(2),
                ["working_set"] = currentProcess.WorkingSet64,
                ["private_memory"] = currentProcess.PrivateMemorySize64,
                ["thread_count"] = currentProcess.Threads.Count
            };

            // Check for potential issues
            var issues = new List<string>();

            // Check if application has been running for a reasonable time
            if (uptime.TotalMinutes < 1)
            {
                issues.Add("Application recently started");
            }

            // Check memory usage
            var workingSetMb = currentProcess.WorkingSet64 / (1024 * 1024);
            if (workingSetMb > 1000) // More than 1GB
            {
                issues.Add($"High memory usage: {workingSetMb}MB");
            }

            // Check thread count
            if (currentProcess.Threads.Count > 500)
            {
                issues.Add($"High thread count: {currentProcess.Threads.Count}");
            }

            // Check GC pressure
            var gen2Collections = GC.CollectionCount(2);
            if (gen2Collections > 100) // Arbitrary threshold
            {
                issues.Add($"High GC Gen2 collections: {gen2Collections}");
            }

            if (issues.Any())
            {
                var issueDescription = string.Join(", ", issues);
                return Task.FromResult(HealthCheckResult.Degraded($"Application issues detected: {issueDescription}", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Application is healthy", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Application health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Application health check failed: {ex.Message}", ex));
        }
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalMinutes >= 1)
            return $"{uptime.Minutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }
}

/// <summary>
/// Health check for memory usage and GC performance
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private readonly ILogger<MemoryHealthCheck> _logger;

    public MemoryHealthCheck(ILogger<MemoryHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var gcInfo = GC.GetGCMemoryInfo();

            var workingSetMb = currentProcess.WorkingSet64 / (1024.0 * 1024.0);
            var privateMemoryMb = currentProcess.PrivateMemorySize64 / (1024.0 * 1024.0);
            var managedMemoryMb = GC.GetTotalMemory(false) / (1024.0 * 1024.0);

            var data = new Dictionary<string, object>
            {
                ["working_set_mb"] = Math.Round(workingSetMb, 2),
                ["private_memory_mb"] = Math.Round(privateMemoryMb, 2),
                ["managed_memory_mb"] = Math.Round(managedMemoryMb, 2),
                ["gc_heap_size_mb"] = Math.Round(gcInfo.HeapSizeBytes / (1024.0 * 1024.0), 2),
                ["gc_fragmented_mb"] = Math.Round(gcInfo.FragmentedBytes / (1024.0 * 1024.0), 2),
                ["gc_gen0_collections"] = GC.CollectionCount(0),
                ["gc_gen1_collections"] = GC.CollectionCount(1),
                ["gc_gen2_collections"] = GC.CollectionCount(2),
                ["gc_memory_load"] = gcInfo.MemoryLoadBytes,
                ["gc_compacting"] = gcInfo.Compacted,
                // ["large_object_heap_size_mb"] = Math.Round(gcInfo.GenerationInfo.Where(g => g.Index == 3).Sum(g => g.SizeAfterBytes) / (1024.0 * 1024.0), 2)
            };

            var issues = new List<string>();

            // Check for memory pressure
            if (workingSetMb > 2000) // More than 2GB
            {
                issues.Add($"High working set: {workingSetMb:F0}MB");
            }

            if (managedMemoryMb > 1000) // More than 1GB managed memory
            {
                issues.Add($"High managed memory: {managedMemoryMb:F0}MB");
            }

            // Check GC efficiency
            var fragmentation = gcInfo.FragmentedBytes / (double)gcInfo.HeapSizeBytes * 100;
            if (fragmentation > 20) // More than 20% fragmentation
            {
                issues.Add($"High memory fragmentation: {fragmentation:F1}%");
            }

            // Check generation 2 collection frequency (indication of memory pressure)
            var gen2Rate = GC.CollectionCount(2) / Math.Max(1, GC.CollectionCount(0)) * 100;
            if (gen2Rate > 10) // More than 10% of collections are Gen2
            {
                issues.Add($"High Gen2 collection rate: {gen2Rate:F1}%");
            }

            if (issues.Any())
            {
                var issueDescription = string.Join(", ", issues);
                _logger.LogWarning("Memory issues detected: {Issues}", issueDescription);
                return Task.FromResult(HealthCheckResult.Degraded($"Memory issues: {issueDescription}", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Memory usage is healthy", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Memory health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Memory health check failed: {ex.Message}", ex));
        }
    }
}

/// <summary>
/// Health check for disk space
/// </summary>
public class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly ILogger<DiskSpaceHealthCheck> _logger;

    public DiskSpaceHealthCheck(ILogger<DiskSpaceHealthCheck> logger)
    {
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToList();
            var driveInfo = new List<object>();
            var issues = new List<string>();

            foreach (var drive in drives)
            {
                var freeSpacePercent = (double)drive.AvailableFreeSpace / drive.TotalSize * 100;
                var freeSpaceGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                var totalSizeGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);

                driveInfo.Add(new
                {
                    name = drive.Name,
                    type = drive.DriveType.ToString(),
                    format = drive.DriveFormat,
                    free_space_gb = Math.Round(freeSpaceGb, 2),
                    total_size_gb = Math.Round(totalSizeGb, 2),
                    free_space_percent = Math.Round(freeSpacePercent, 1)
                });

                // Check for low disk space
                if (freeSpacePercent < 10) // Less than 10% free
                {
                    issues.Add($"Low disk space on {drive.Name}: {freeSpacePercent:F1}% free");
                }
                else if (freeSpaceGb < 1) // Less than 1GB free
                {
                    issues.Add($"Very low disk space on {drive.Name}: {freeSpaceGb:F1}GB free");
                }
            }

            var data = new Dictionary<string, object>
            {
                ["drives"] = driveInfo,
                ["total_drives"] = drives.Count
            };

            if (issues.Any())
            {
                var issueDescription = string.Join(", ", issues);
                _logger.LogWarning("Disk space issues detected: {Issues}", issueDescription);
                return Task.FromResult(HealthCheckResult.Degraded($"Disk space issues: {issueDescription}", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy("Disk space is healthy", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Disk space health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy($"Disk space health check failed: {ex.Message}", ex));
        }
    }
}

/// <summary>
/// Health check for circuit breaker status
/// </summary>
public class CircuitBreakerHealthCheck : IHealthCheck
{
    private readonly ICircuitBreakerFactory _circuitBreakerFactory;
    private readonly ILogger<CircuitBreakerHealthCheck> _logger;

    public CircuitBreakerHealthCheck(
        ICircuitBreakerFactory circuitBreakerFactory,
        ILogger<CircuitBreakerHealthCheck> logger)
    {
        _circuitBreakerFactory = circuitBreakerFactory;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = _circuitBreakerFactory.GetAllStatistics();
            var circuitBreakerInfo = new List<object>();
            var issues = new List<string>();

            foreach (var kvp in statistics)
            {
                var name = kvp.Key;
                var stats = kvp.Value;

                circuitBreakerInfo.Add(new
                {
                    name = name,
                    state = stats.State.ToString(),
                    success_count = stats.SuccessCount,
                    failure_count = stats.FailureCount,
                    success_rate = Math.Round(stats.SuccessRate, 2),
                    trip_count = stats.TripCount,
                    consecutive_failures = stats.ConsecutiveFailures,
                    last_error = stats.LastError
                });

                // Check for issues
                if (stats.State == CircuitBreakerState.Open)
                {
                    issues.Add($"Circuit breaker '{name}' is OPEN");
                }
                else if (stats.FailureRate > 50 && stats.TotalCount > 10)
                {
                    issues.Add($"Circuit breaker '{name}' has high failure rate: {stats.FailureRate:F1}%");
                }
            }

            var data = new Dictionary<string, object>
            {
                ["circuit_breakers"] = circuitBreakerInfo,
                ["total_circuit_breakers"] = statistics.Count
            };

            if (issues.Any())
            {
                var issueDescription = string.Join(", ", issues);
                _logger.LogWarning("Circuit breaker issues detected: {Issues}", issueDescription);
                return Task.FromResult(HealthCheckResult.Degraded($"Circuit breaker issues: {issueDescription}", data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy("All circuit breakers are healthy", data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Circuit breaker health check failed");
            return Task.FromResult(HealthCheckResult.Degraded($"Circuit breaker health check failed: {ex.Message}", ex));
        }
    }
}