using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Infrastructure.HealthChecks;

/// <summary>
/// Health check for database connectivity and basic operations
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly DbContext _dbContext;
    private readonly ILogger<DatabaseHealthCheck> _logger;

    public DatabaseHealthCheck(
        DbContext dbContext,
        ILogger<DatabaseHealthCheck> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Try to open connection and execute a simple query
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

            stopwatch.Stop();

            if (!canConnect)
            {
                _logger.LogWarning("Database connection check failed");
                return HealthCheckResult.Unhealthy("Cannot connect to database");
            }

            var data = new Dictionary<string, object>
            {
                ["responseTime"] = $"{stopwatch.ElapsedMilliseconds}ms",
                ["provider"] = _dbContext.Database.ProviderName ?? "Unknown",
                ["database"] = _dbContext.Database.GetDbConnection().Database ?? "Unknown"
            };

            // Check if response time is acceptable
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning("Database response time is slow: {ElapsedMilliseconds}ms", 
                    stopwatch.ElapsedMilliseconds);
                return HealthCheckResult.Degraded("Database is slow", data: data);
            }

            _logger.LogDebug("Database health check completed in {ElapsedMilliseconds}ms", 
                stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Healthy("Database is accessible", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return HealthCheckResult.Unhealthy("Database check failed", ex);
        }
    }
}

/// <summary>
/// Generic database health check that can work with any DbContext type
/// </summary>
public class DatabaseHealthCheck<TContext> : IHealthCheck where TContext : DbContext
{
    private readonly TContext _dbContext;
    private readonly ILogger<DatabaseHealthCheck<TContext>> _logger;

    public DatabaseHealthCheck(
        TContext dbContext,
        ILogger<DatabaseHealthCheck<TContext>> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Try to open connection and execute a simple query
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);

            stopwatch.Stop();

            if (!canConnect)
            {
                _logger.LogWarning("Database connection check failed for {ContextType}", typeof(TContext).Name);
                return HealthCheckResult.Unhealthy($"Cannot connect to database ({typeof(TContext).Name})");
            }

            var data = new Dictionary<string, object>
            {
                ["responseTime"] = $"{stopwatch.ElapsedMilliseconds}ms",
                ["contextType"] = typeof(TContext).Name,
                ["provider"] = _dbContext.Database.ProviderName ?? "Unknown",
                ["database"] = _dbContext.Database.GetDbConnection().Database ?? "Unknown"
            };

            // Try to get pending migrations count
            try
            {
                var pendingMigrations = await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
                var pendingCount = pendingMigrations.Count();
                data["pendingMigrations"] = pendingCount;

                if (pendingCount > 0)
                {
                    _logger.LogWarning("Database has {PendingMigrations} pending migrations", pendingCount);
                    return HealthCheckResult.Degraded($"Database has {pendingCount} pending migrations", data: data);
                }
            }
            catch (Exception migrationEx)
            {
                _logger.LogDebug(migrationEx, "Could not check for pending migrations");
                // This is not critical, continue with health check
            }

            // Check if response time is acceptable
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning("Database response time is slow for {ContextType}: {ElapsedMilliseconds}ms", 
                    typeof(TContext).Name, stopwatch.ElapsedMilliseconds);
                return HealthCheckResult.Degraded("Database is slow", data: data);
            }

            _logger.LogDebug("Database health check completed for {ContextType} in {ElapsedMilliseconds}ms", 
                typeof(TContext).Name, stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Healthy("Database is accessible", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed for {ContextType}", typeof(TContext).Name);
            return HealthCheckResult.Unhealthy($"Database check failed ({typeof(TContext).Name})", ex);
        }
    }
}