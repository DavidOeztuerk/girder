using Girder.Abstractions.Caching;
using Girder.Infrastructure.Caching.Http;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Observability;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Headers;
using Girder.Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Girder.Infrastructure.Builder.Modules;

/// <summary>
/// Middleware extension methods on InfrastructureMiddlewareBuilder.
/// Each method mirrors exactly one middleware registration from the legacy UseSharedInfrastructure.
/// </summary>
public static class MiddlewarePipelineModule
{
    public static InfrastructureMiddlewareBuilder UseSecurityHeaders(this InfrastructureMiddlewareBuilder builder)
    {
        builder.Requires<ISecurityHeadersService>("UseSecurityHeaders()", "AddSecurityHeaders()");
        builder.App.UseMiddleware<SecurityHeadersMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseCorrelationId(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseMiddleware<CorrelationIdMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseRequestLogging(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseMiddleware<RequestLoggingMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseTelemetry(this InfrastructureMiddlewareBuilder builder)
    {
        builder.Requires<IPerformanceMetrics>("UseTelemetry()", "AddObservability()");
        builder.App.UseTelemetry();
        builder.App.UsePerformanceMonitoring();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseExceptionHandling(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseInputSanitization(this InfrastructureMiddlewareBuilder builder)
    {
        builder.Requires<IInputSanitizer>("UseInputSanitization()", "AddInputSanitization()");
        builder.App.UseMiddleware<InputSanitizationMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseSerilogLogging(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? "");
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.FirstOrDefault() ?? "");
                diagnosticContext.Set("RemoteIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "");

                if (httpContext.User?.Identity?.IsAuthenticated == true)
                {
                    diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value ?? "");
                }
            };
        });
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseCors(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseCors();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseSwagger(this InfrastructureMiddlewareBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.App.UseSwaggerDocumentation(builder.ServiceName);
        }
        return builder;
    }

    /// <summary>
    /// Adds distributed rate limiting to the pipeline. Requires an
    /// <c>IDistributedRateLimitStore</c>, registered by <c>AddCaching</c>.
    /// </summary>
    public static InfrastructureMiddlewareBuilder UseRateLimiting(this InfrastructureMiddlewareBuilder builder)
    {
        builder.Requires<IDistributedRateLimitStore>(
            "UseRateLimiting()", "AddInMemoryCache(prefix) or AddRedisCache(prefix)");
        builder.App.UseMiddleware<DistributedRateLimitingMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseHealthCheckEndpoints(this InfrastructureMiddlewareBuilder builder)
    {
        var healthCheckOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var response = new
                {
                    status = report.Status.ToString(),
                    timestamp = DateTime.UtcNow,
                    durationMs = report.TotalDuration.TotalMilliseconds,
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        durationMs = e.Value.Duration.TotalMilliseconds,
                        tags = e.Value.Tags,
                        error = e.Value.Exception?.Message
                    })
                };
                await context.Response.WriteAsync(
                    System.Text.Json.JsonSerializer.Serialize(response,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            }
        };

        builder.App.UseHealthChecks("/health", healthCheckOptions);

        builder.App.UseHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live"),
            ResponseWriter = healthCheckOptions.ResponseWriter,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status200OK
            }
        });

        builder.App.UseHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready"),
            ResponseWriter = healthCheckOptions.ResponseWriter,
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        });

        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseAuth(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseAuthentication();
        builder.App.UseAuthorization();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseSecurityAudit(this InfrastructureMiddlewareBuilder builder)
    {
        builder.Requires<ISecurityAuditLogger>("UseSecurityAudit()", "AddAuditLogging()");
        builder.App.UseMiddleware<SecurityAuditMiddleware>();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UsePermissions(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UsePermissionMiddleware();
        return builder;
    }

    /// <summary>
    /// Refuses tokens that were withdrawn. Place after <see cref="UseAuth"/>,
    /// which establishes the claims it reads.
    /// </summary>
    /// <remarks>
    /// Fails while composing when no evaluator is registered — a revocation
    /// check that silently answers "not revoked" is indistinguishable from one
    /// that works.
    /// </remarks>
    public static InfrastructureMiddlewareBuilder UseTokenRevocation(
        this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseTokenRevocation();
        return builder;
    }

    public static InfrastructureMiddlewareBuilder UseHttpCaching(this InfrastructureMiddlewareBuilder builder)
    {
        builder.Requires<ICachePolicyProvider>("UseHttpCaching()", "AddCaching()");
        builder.App.UseHttpResponseCachingHeaders();
        return builder;
    }
}
