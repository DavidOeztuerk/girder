using Girder.Abstractions.Caching;
using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Caching.Http;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Observability;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Headers;
using Girder.Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Girder.Infrastructure.Builder.Modules;

/// <summary>
/// One pipeline step per method, on <see cref="InfrastructureMiddlewareBuilder"/>.
/// </summary>
/// <remarks>
/// Each step names the module it belongs to and adds nothing when that module was
/// left out — so <c>Without(module, reason)</c> is decided once, on the service
/// side, and holds here too.
/// </remarks>
public static class MiddlewarePipelineModule
{
    /// <summary>The response headers a browser is told to enforce.</summary>
    public static InfrastructureMiddlewareBuilder UseSecurityHeaders(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.SecurityHeaders, step =>
        {
            step.Requires<ISecurityHeadersService>("UseSecurityHeaders()", "AddSecurityHeaders()");
            step.App.UseMiddleware<SecurityHeadersMiddleware>();
        });

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

    /// <summary>Traces and the performance counters that hang off them.</summary>
    public static InfrastructureMiddlewareBuilder UseTelemetry(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.Observability, step =>
        {
            step.Requires<IPerformanceMetrics>("UseTelemetry()", "AddObservability()");
            step.App.UseTelemetry();
            step.App.UsePerformanceMonitoring();
        });

    public static InfrastructureMiddlewareBuilder UseExceptionHandling(this InfrastructureMiddlewareBuilder builder)
    {
        builder.App.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        return builder;
    }

    /// <summary>Refuses requests carrying injection syntax.</summary>
    public static InfrastructureMiddlewareBuilder UseInputSanitization(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.InputSanitization, step =>
        {
            step.Requires<IInputSanitizer>("UseInputSanitization()", "AddInputSanitization()");
            step.App.UseMiddleware<InputSanitizationMiddleware>();
        });

    /// <summary>Serilog's own request log line.</summary>
    public static InfrastructureMiddlewareBuilder UseSerilogLogging(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.Logging, step =>
        step.App.UseSerilogRequestLogging(options =>
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
        }));

    /// <summary>The cross-origin rules the service was configured with.</summary>
    public static InfrastructureMiddlewareBuilder UseCors(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.Cors, step => step.App.UseCors());

    /// <summary>Swagger, in development only.</summary>
    public static InfrastructureMiddlewareBuilder UseSwagger(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.ApiDocumentation, step =>
        {
            if (step.Environment.IsDevelopment())
            {
                step.App.UseSwaggerDocumentation(step.ServiceName);
            }
        });

    /// <summary>
    /// Adds distributed rate limiting to the pipeline. Requires an
    /// <c>IDistributedRateLimitStore</c>, registered by <c>AddCaching</c>.
    /// </summary>
    public static InfrastructureMiddlewareBuilder UseRateLimiting(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.RateLimiting, step =>
        {
            step.Requires<IDistributedRateLimitStore>(
                "UseRateLimiting()", "AddInMemoryCache(prefix) or AddRedisCache(prefix)");
            step.App.UseMiddleware<DistributedRateLimitingMiddleware>();
        });

    /// <summary>Liveness and readiness endpoints.</summary>
    public static InfrastructureMiddlewareBuilder UseHealthCheckEndpoints(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.HealthChecks, step =>
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

        step.App.UseHealthChecks("/health", healthCheckOptions);

        step.App.UseHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
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

        step.App.UseHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
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
        });

    /// <summary>Authentication, then authorization.</summary>
    /// <remarks>
    /// Not gated on a module: which scheme establishes a caller is not something
    /// the module set decides — <c>UseJwt(...)</c> answers it, and so does any
    /// <c>AddAuthentication(...)</c> the service wrote itself. What it does do is
    /// say which call is missing, rather than letting the framework report an
    /// unresolvable <c>IAuthenticationSchemeProvider</c> naming a type the reader
    /// never wrote.
    /// </remarks>
    public static InfrastructureMiddlewareBuilder UseAuth(this InfrastructureMiddlewareBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Requires<IAuthenticationSchemeProvider>(
            "UseAuth()", "UseJwt(...) while configuring Girder, or AddAuthentication(...)");
        builder.App.UseAuthentication();
        builder.App.UseAuthorization();
        return builder;
    }

    /// <summary>The security audit trail.</summary>
    public static InfrastructureMiddlewareBuilder UseSecurityAudit(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.Audit, step =>
        {
            step.Requires<ISecurityAuditLogger>("UseSecurityAudit()", "AddAuditLogging()");
            step.App.UseMiddleware<SecurityAuditMiddleware>();
        });

    /// <summary>
    /// Refuses every request no permission covers.
    /// </summary>
    /// <remarks>
    /// Fail-closed, and right as a default for a service whose whole surface is
    /// behind a token. A service with a public surface leaves it out with
    /// <c>Without(GirderModule.PermissionEnforcement, reason)</c> and keeps
    /// <see cref="GirderModule.Authorization"/>, which is what answers
    /// <c>[RequirePermission]</c> on the endpoints that carry it.
    /// </remarks>
    public static InfrastructureMiddlewareBuilder UsePermissions(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.PermissionEnforcement, step => step.App.UsePermissionMiddleware());

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

    /// <summary>
    /// ETags and conditional responses.
    /// </summary>
    /// <remarks>
    /// <see cref="GirderModule.HttpResponseCaching"/> is not in the default set,
    /// so this step is in the default chain but does nothing until a service asks
    /// for the module. It used to run unconditionally, which made the default
    /// chain contradict the default modules.
    /// </remarks>
    public static InfrastructureMiddlewareBuilder UseHttpCaching(this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.HttpResponseCaching, step =>
        {
            step.Requires<ICachePolicyProvider>("UseHttpCaching()", "AddCaching()");
            step.App.UseHttpResponseCachingHeaders();
        });
}
