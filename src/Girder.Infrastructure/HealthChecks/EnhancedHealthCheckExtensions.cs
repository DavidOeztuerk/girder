using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Infrastructure.HealthChecks;

/// <summary>
/// Extension methods for enhanced health checks
/// </summary>
public static class EnhancedHealthCheckExtensions
{
    /// <summary>
    /// Add comprehensive health checks for the Skillswap platform
    /// </summary>
    public static IServiceCollection AddSkillswapHealthChecks(
        this IServiceCollection services,
        Action<HealthCheckBuilder>? configure = null)
    {
        var builder = new HealthCheckBuilder(services);

        // Add standard health checks
        builder
            .AddDatabaseHealthCheck()
            .AddRedisHealthCheck()
            .AddRabbitMqHealthCheck()
            .AddExternalApiHealthChecks()
            .AddCustomHealthChecks();

        // Allow custom configuration
        configure?.Invoke(builder);

        return services;
    }

    /// <summary>
    /// Use enhanced health check endpoints
    /// </summary>
    public static IApplicationBuilder UseEnhancedHealthChecks(this IApplicationBuilder app)
    {
        // Liveness probe (basic health check)
        app.UseHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteHealthCheckResponse,
            AllowCachingResponses = false
        });

        // Readiness probe (comprehensive health check)
        app.UseHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteHealthCheckResponse,
            AllowCachingResponses = false
        });

        // Detailed health report
        app.UseHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteDetailedHealthCheckResponse,
            AllowCachingResponses = false
        });

        // Health check UI (for development)
        app.UseHealthChecks("/health/ui", new HealthCheckOptions
        {
            ResponseWriter = WriteHealthCheckUI,
            AllowCachingResponses = false
        });

        return app;
    }

    private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration,
            info = report.Entries.Where(e => e.Value.Status == HealthStatus.Healthy)
                .ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString() }),
            error = report.Entries.Where(e => e.Value.Status == HealthStatus.Unhealthy)
                .ToDictionary(e => e.Key, e => new
                {
                    status = e.Value.Status.ToString(),
                    error = e.Value.Exception?.Message
                }),
            details = report.Entries.Where(e => e.Value.Status == HealthStatus.Degraded)
                .ToDictionary(e => e.Key, e => new
                {
                    status = e.Value.Status.ToString(),
                    error = e.Value.Exception?.Message
                })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private static async Task WriteDetailedHealthCheckResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            duration = report.TotalDuration.TotalMilliseconds,
            results = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds,
                    error = e.Value.Exception?.Message,
                    data = e.Value.Data?.ToDictionary(d => d.Key, d => d.Value)
                })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    private static async Task WriteHealthCheckUI(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "text/html";

        var html = GenerateHealthCheckHtml(report);
        await context.Response.WriteAsync(html);
    }

    private static string GenerateHealthCheckHtml(HealthReport report)
    {
        var statusColor = report.Status switch
        {
            HealthStatus.Healthy => "green",
            HealthStatus.Degraded => "orange",
            HealthStatus.Unhealthy => "red",
            _ => "gray"
        };

        var rows = string.Join("", report.Entries.Select(entry =>
        {
            var color = entry.Value.Status switch
            {
                HealthStatus.Healthy => "green",
                HealthStatus.Degraded => "orange",
                HealthStatus.Unhealthy => "red",
                _ => "gray"
            };

            return $@"
                <tr>
                    <td>{entry.Key}</td>
                    <td style='color: {color}; font-weight: bold;'>{entry.Value.Status}</td>
                    <td>{entry.Value.Duration.TotalMilliseconds:F1}ms</td>
                    <td>{entry.Value.Exception?.Message ?? "-"}</td>
                </tr>";
        }));

        return $@"
            <!DOCTYPE html>
            <html>
            <head>
                <title>Skillswap Health Check</title>
                <style>
                    body {{ font-family: Arial, sans-serif; margin: 40px; }}
                    .status {{ font-size: 24px; font-weight: bold; color: {statusColor}; }}
                    table {{ border-collapse: collapse; width: 100%; margin-top: 20px; }}
                    th, td {{ border: 1px solid #ddd; padding: 8px; text-align: left; }}
                    th {{ background-color: #f2f2f2; }}
                    .refresh {{ margin-top: 20px; }}
                </style>
                <script>
                    function autoRefresh() {{
                        setTimeout(function(){{ location.reload(); }}, 30000);
                    }}
                </script>
            </head>
            <body onload='autoRefresh()'>
                <h1>Skillswap Health Check</h1>
                <div class='status'>Status: {report.Status}</div>
                <p>Last Updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
                <p>Total Duration: {report.TotalDuration.TotalMilliseconds:F1}ms</p>
                
                <table>
                    <thead>
                        <tr>
                            <th>Check Name</th>
                            <th>Status</th>
                            <th>Duration</th>
                            <th>Error</th>
                        </tr>
                    </thead>
                    <tbody>
                        {rows}
                    </tbody>
                </table>
                
                <div class='refresh'>
                    <p><em>This page auto-refreshes every 30 seconds</em></p>
                    <button onclick='location.reload()'>Refresh Now</button>
                </div>
            </body>
            </html>";
    }
}

/// <summary>
/// Builder for configuring health checks
/// </summary>
public class HealthCheckBuilder
{
    private readonly IServiceCollection _services;
    private readonly IHealthChecksBuilder _healthChecksBuilder;

    public HealthCheckBuilder(IServiceCollection services)
    {
        _services = services;
        _healthChecksBuilder = services.AddHealthChecks();
    }

    /// <summary>
    /// Add database health checks
    /// </summary>
    public HealthCheckBuilder AddDatabaseHealthCheck()
    {
        //_healthChecksBuilder
        //    .AddCheck<DatabaseHealthCheck>("database",
        //        HealthStatus.Unhealthy,
        //        tags: new[] { "ready", "database" })
        //    .AddCheck<DatabaseConnectionPoolHealthCheck>("database_pool",
        //        HealthStatus.Degraded,
        //        tags: new[] { "ready", "database" });

        return this;
    }

    /// <summary>
    /// Add Redis health checks
    /// </summary>
    public HealthCheckBuilder AddRedisHealthCheck()
    {
        _healthChecksBuilder
            .AddCheck<RedisHealthCheck>("redis",
                HealthStatus.Degraded,
                tags: new[] { "ready", "cache" })
            .AddCheck<RedisPerformanceHealthCheck>("redis_performance",
                HealthStatus.Degraded,
                tags: new[] { "cache" });

        return this;
    }

    /// <summary>
    /// Add RabbitMQ health checks
    /// </summary>
    public HealthCheckBuilder AddRabbitMqHealthCheck()
    {
        // _healthChecksBuilder
        //     .AddCheck<RabbitMqHealthCheck>("rabbitmq",
        //         HealthStatus.Unhealthy,
        //         tags: new[] { "ready", "messaging" })
        //     .AddCheck<RabbitMqQueueHealthCheck>("rabbitmq_queues",
        //         HealthStatus.Degraded,
        //         tags: new[] { "messaging" });

        return this;
    }

    /// <summary>
    /// Add external API health checks
    /// </summary>
    public HealthCheckBuilder AddExternalApiHealthChecks()
    {
        // _healthChecksBuilder
        //     .AddCheck<EmailServiceHealthCheck>("email_service",
        //         HealthStatus.Degraded,
        //         tags: new[] { "external", "email" })
        //     .AddCheck<FileStorageHealthCheck>("file_storage",
        //         HealthStatus.Degraded,
        //         tags: new[] { "external", "storage" });

        return this;
    }

    /// <summary>
    /// Add custom application health checks
    /// </summary>
    public HealthCheckBuilder AddCustomHealthChecks()
    {
        _healthChecksBuilder
            .AddCheck<ApplicationHealthCheck>("application",
                HealthStatus.Unhealthy,
                tags: new[] { "live", "ready" })
            .AddCheck<CircuitBreakerHealthCheck>("circuit_breakers",
                HealthStatus.Degraded,
                tags: new[] { "resilience" })
            .AddCheck<MemoryHealthCheck>("memory",
                HealthStatus.Degraded,
                tags: new[] { "live", "performance" })
            .AddCheck<DiskSpaceHealthCheck>("disk_space",
                HealthStatus.Degraded,
                tags: new[] { "live", "performance" });

        return this;
    }

    /// <summary>
    /// Add a custom health check
    /// </summary>
    public HealthCheckBuilder AddCustomCheck<T>(
        string name,
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        params string[] tags)
        where T : class, IHealthCheck
    {
        _healthChecksBuilder.AddCheck<T>(name, failureStatus, tags);
        return this;
    }

    /// <summary>
    /// Add health check publishing (optional)
    /// </summary>
    public HealthCheckBuilder AddPublisher<T>()
        where T : class, IHealthCheckPublisher
    {
        _services.AddSingleton<IHealthCheckPublisher, T>();
        return this;
    }
}