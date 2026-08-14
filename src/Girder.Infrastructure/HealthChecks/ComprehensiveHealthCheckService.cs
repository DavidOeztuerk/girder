using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.HealthChecks;

public class ComprehensiveHealthCheckService : IComprehensiveHealthCheckService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComprehensiveHealthCheckService> _logger;
    private readonly Dictionary<string, Func<Task<HealthCheckResult>>> _healthChecks;

    public ComprehensiveHealthCheckService(
        IServiceProvider serviceProvider,
        ILogger<ComprehensiveHealthCheckService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthChecks = new Dictionary<string, Func<Task<HealthCheckResult>>>();
    }

    public void RegisterHealthCheck(string name, Func<Task<HealthCheckResult>> healthCheck)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Health check name cannot be null or empty", nameof(name));
        
        if (healthCheck == null)
            throw new ArgumentNullException(nameof(healthCheck));

        _healthChecks[name] = healthCheck;
        _logger.LogDebug("Registered health check: {HealthCheckName}", name);
    }

    public async Task<HealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default)
    {
        var entries = new Dictionary<string, HealthReportEntry>();
        var overallStatus = HealthStatus.Healthy;
        var totalDuration = TimeSpan.Zero;

        foreach (var kvp in _healthChecks)
        {
            var name = kvp.Key;
            var check = kvp.Value;

            try
            {
                var stopwatch = Stopwatch.StartNew();
                var result = await check();
                stopwatch.Stop();

                var entry = new HealthReportEntry(
                    status: result.Status,
                    description: result.Description,
                    duration: stopwatch.Elapsed,
                    exception: result.Exception,
                    data: result.Data);

                entries[name] = entry;
                totalDuration += stopwatch.Elapsed;

                // Update overall status
                if (result.Status == HealthStatus.Unhealthy)
                    overallStatus = HealthStatus.Unhealthy;
                else if (result.Status == HealthStatus.Degraded && overallStatus != HealthStatus.Unhealthy)
                    overallStatus = HealthStatus.Degraded;

                _logger.LogDebug("Health check {Name} completed with status {Status} in {Duration}ms",
                    name, result.Status, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check {Name} failed with exception", name);
                
                entries[name] = new HealthReportEntry(
                    status: HealthStatus.Unhealthy,
                    description: $"Health check failed: {ex.Message}",
                    duration: TimeSpan.Zero,
                    exception: ex,
                    data: null);
                
                overallStatus = HealthStatus.Unhealthy;
            }
        }

        return new HealthReport(entries, overallStatus, totalDuration);
    }

    public async Task<ComprehensiveHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var results = new Dictionary<string, HealthCheckResult>();
        var overallStatus = HealthStatus.Healthy;

        foreach (var kvp in _healthChecks)
        {
            var name = kvp.Key;
            var check = kvp.Value;

            try
            {
                var result = await check();
                results[name] = result;

                // Update overall status
                if (result.Status == HealthStatus.Unhealthy)
                {
                    overallStatus = HealthStatus.Unhealthy;
                }
                else if (result.Status == HealthStatus.Degraded && overallStatus != HealthStatus.Unhealthy)
                {
                    overallStatus = HealthStatus.Degraded;
                }

                _logger.LogDebug("Health check {Name} completed with status {Status}", name, result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check {Name} failed", name);
                results[name] = HealthCheckResult.Unhealthy($"Check failed: {ex.Message}", ex);
                overallStatus = HealthStatus.Unhealthy;
            }
        }

        stopwatch.Stop();

        return new ComprehensiveHealthCheckResult
        {
            Status = overallStatus,
            Results = results,
            TotalDuration = stopwatch.Elapsed,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        var result = await CheckHealthAsync(cancellationToken);
        return result.Status == HealthStatus.Healthy;
    }

    public async Task<HealthCheckResult> CheckDatabaseHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get a database health check from the service provider
            var dbHealthCheck = _serviceProvider.GetService<DatabaseHealthCheck>();
            if (dbHealthCheck != null)
            {
                var context = new HealthCheckContext
                {
                    Registration = new HealthCheckRegistration("Database", dbHealthCheck, null, null)
                };
                return await dbHealthCheck.CheckHealthAsync(context, cancellationToken);
            }

            return HealthCheckResult.Healthy("No database health check configured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }

    public async Task<HealthCheckResult> CheckRedisHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get a Redis health check from the service provider
            var redisHealthCheck = _serviceProvider.GetService<RedisHealthCheck>();
            if (redisHealthCheck != null)
            {
                var context = new HealthCheckContext
                {
                    Registration = new HealthCheckRegistration("Redis", redisHealthCheck, null, null)
                };
                return await redisHealthCheck.CheckHealthAsync(context, cancellationToken);
            }

            return HealthCheckResult.Healthy("No Redis health check configured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy("Redis check failed", ex);
        }
    }

    public async Task<HealthCheckResult> CheckRabbitMQHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get a RabbitMQ health check from the service provider
            var rabbitHealthCheck = _serviceProvider.GetService<RabbitMQHealthCheck>();
            if (rabbitHealthCheck != null)
            {
                var context = new HealthCheckContext
                {
                    Registration = new HealthCheckRegistration("RabbitMQ", rabbitHealthCheck, null, null)
                };
                return await rabbitHealthCheck.CheckHealthAsync(context, cancellationToken);
            }

            return HealthCheckResult.Healthy("No RabbitMQ health check configured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ health check failed");
            return HealthCheckResult.Unhealthy("RabbitMQ check failed", ex);
        }
    }
}

public class ComprehensiveHealthCheckResult
{
    public HealthStatus Status { get; set; }
    public Dictionary<string, HealthCheckResult> Results { get; set; } = new();
    public TimeSpan TotalDuration { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}