using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.HealthChecks;

public interface IComprehensiveHealthCheckService
{
    /// <summary>
    /// Register a custom health check
    /// </summary>
    void RegisterHealthCheck(string name, Func<Task<HealthCheckResult>> healthCheck);

    /// <summary>
    /// Get a comprehensive health report for all registered checks
    /// </summary>
    Task<HealthReport> GetHealthReportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check the overall health of all registered components
    /// </summary>
    Task<ComprehensiveHealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if all registered components are healthy
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check database health specifically
    /// </summary>
    Task<HealthCheckResult> CheckDatabaseHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check Redis health specifically
    /// </summary>
    Task<HealthCheckResult> CheckRedisHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check RabbitMQ health specifically
    /// </summary>
    Task<HealthCheckResult> CheckRabbitMQHealthAsync(CancellationToken cancellationToken = default);
}