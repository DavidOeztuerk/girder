using Girder.Infrastructure.Security.Audit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Girder.Infrastructure.Security.Headers;

/// <summary>
/// Middleware to add security headers to HTTP responses
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISecurityHeadersService _securityHeadersService;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;
    private readonly SecurityHeadersMiddlewareOptions _options;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        ISecurityHeadersService securityHeadersService,
        ILogger<SecurityHeadersMiddleware> logger,
        IOptions<SecurityHeadersMiddlewareOptions> options)
    {
        _next = next;
        _securityHeadersService = securityHeadersService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableSecurityHeaders || ShouldSkipSecurityHeaders(context))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Create security headers context
            var securityContext = CreateSecurityHeadersContext(context);

            // Generate nonces for inline content if needed
            if (securityContext.HasInlineScripts || securityContext.HasInlineStyles)
            {
                GenerateNonces(context, securityContext);
            }

            // Get security headers
            var headers = _securityHeadersService.GetSecurityHeaders(securityContext);

            // Apply security headers before calling next middleware
            ApplySecurityHeaders(context, headers);

            // Log security headers applied
            if (_options.LogSecurityHeaders)
            {
                _logger.LogDebug("Applied {HeaderCount} security headers for {Path}",
                    headers.Count, context.Request.Path);
            }

            await _next(context);

            // Post-process response headers if needed
            await PostProcessResponseHeaders(context, securityContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying security headers");

            // Apply minimal security headers on error
            if (_options.ApplyMinimalHeadersOnError)
            {
                ApplyMinimalSecurityHeaders(context);
            }

            await _next(context);
        }
        finally
        {
            stopwatch.Stop();

            // Record performance metrics if available
            try
            {
                var performanceMetrics = context.RequestServices.GetService<Girder.Infrastructure.Observability.IPerformanceMetrics>();
                var normalizedEndpoint = Girder.Infrastructure.Observability.PerformanceMiddleware.GetNormalizedPath(context.Request.Path);
                performanceMetrics?.RecordSecurityMiddlewarePerformance("security_headers", normalizedEndpoint, stopwatch.Elapsed.TotalMilliseconds);
            }
            catch
            {
                // Ignore metrics errors
            }
        }
    }

    private SecurityHeadersContext CreateSecurityHeadersContext(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        var securityContext = new SecurityHeadersContext
        {
            Path = path,
            IsApiRequest = IsApiRequest(context),
            IsStaticContent = IsStaticContent(context),
            HasInlineScripts = _options.DetectInlineContent && WillHaveInlineScripts(context),
            HasInlineStyles = _options.DetectInlineContent && WillHaveInlineStyles(context)
        };

        // Add allowed domains from configuration
        securityContext.AllowedDomains.AddRange(_options.AllowedDomains);
        securityContext.CdnDomains.AddRange(_options.CdnDomains);

        // Add custom requirements from context
        foreach (var requirement in _options.CustomRequirements)
        {
            securityContext.CustomRequirements[requirement.Key] = requirement.Value;
        }

        // Add context-specific requirements
        AddContextSpecificRequirements(context, securityContext);

        return securityContext;
    }

    private void GenerateNonces(HttpContext context, SecurityHeadersContext securityContext)
    {
        if (securityContext.HasInlineScripts)
        {
            var scriptNonce = _securityHeadersService.GenerateNonce();
            securityContext.Nonces["script"] = scriptNonce;
            context.Items["ScriptNonce"] = scriptNonce;
        }

        if (securityContext.HasInlineStyles)
        {
            var styleNonce = _securityHeadersService.GenerateNonce();
            securityContext.Nonces["style"] = styleNonce;
            context.Items["StyleNonce"] = styleNonce;
        }
    }

    private static void ApplySecurityHeaders(HttpContext context, Dictionary<string, string> headers)
    {
        foreach (var header in headers)
        {
            // Don't override existing headers unless explicitly configured
            if (!context.Response.Headers.ContainsKey(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }
    }

    private void ApplyMinimalSecurityHeaders(HttpContext context)
    {
        var minimalHeaders = new Dictionary<string, string>
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["X-XSS-Protection"] = "1; mode=block"
        };

        ApplySecurityHeaders(context, minimalHeaders);
    }

    private async Task PostProcessResponseHeaders(HttpContext context, SecurityHeadersContext securityContext)
    {
        try
        {
            // Validate CSP if present
            if (context.Response.Headers.TryGetValue("Content-Security-Policy", out var cspValue))
            {
                var validationResult = _securityHeadersService.ValidateContentSecurityPolicy(cspValue!);

                if (!validationResult.IsValid && _options.LogCspValidationErrors)
                {
                    _logger.LogWarning("CSP validation failed for {Path}: {Errors}",
                        context.Request.Path, string.Join(", ", validationResult.Errors));
                }

                if (validationResult.SecurityScore < _options.MinimumCspScore)
                {
                    _logger.LogWarning("CSP security score {Score} below minimum {MinScore} for {Path}",
                        validationResult.SecurityScore, _options.MinimumCspScore, context.Request.Path);
                }
            }

            // Log security headers analysis if enabled
            if (_options.AnalyzeSecurityHeaders)
            {
                await AnalyzeResponseSecurityHeaders(context);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in post-processing security headers");
        }
    }

    private async Task AnalyzeResponseSecurityHeaders(HttpContext context)
    {
        try
        {
            var responseHeaders = context.Response.Headers.ToDictionary(
                h => h.Key,
                h => h.Value.ToString()
            );

            var analysisResult = _securityHeadersService.AnalyzeSecurityHeaders(responseHeaders);

            if (analysisResult.OverallScore < _options.MinimumSecurityScore)
            {
                _logger.LogWarning(
                    "Security headers score {Score} below minimum {MinScore} for {Path}. Missing: {MissingHeaders}",
                    analysisResult.OverallScore, _options.MinimumSecurityScore, context.Request.Path,
                    string.Join(", ", analysisResult.MissingHeaders));
            }

            if (analysisResult.Vulnerabilities.Any(v => v.Severity >= SecurityVulnerabilitySeverity.High))
            {
                var criticalVulns = analysisResult.Vulnerabilities
                    .Where(v => v.Severity >= SecurityVulnerabilitySeverity.High)
                    .Select(v => v.Description);

                _logger.LogWarning("Critical security header vulnerabilities detected for {Path}: {Vulnerabilities}",
                    context.Request.Path, string.Join(", ", criticalVulns));
            }

            // Log to security audit system if available
            if (_options.LogToAuditSystem && analysisResult.OverallScore < _options.MinimumSecurityScore)
            {
                var auditService = context.RequestServices.GetService<ISecurityAuditService>();
                if (auditService != null)
                {
                    await auditService.LogSecurityEventAsync(
                        "SecurityHeadersAnalysis",
                        $"Security headers score below threshold: {analysisResult.OverallScore}",
                        Audit.SecurityEventSeverity.Medium,
                        new
                        {
                            Path = context.Request.Path.Value,
                            Score = analysisResult.OverallScore,
                            MissingHeaders = analysisResult.MissingHeaders,
                            Vulnerabilities = analysisResult.Vulnerabilities.Count
                        });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing security headers");
        }
    }

    private void AddContextSpecificRequirements(HttpContext context, SecurityHeadersContext securityContext)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Add specific domains for certain paths
        if (path.Contains("/oauth") || path.Contains("/auth"))
        {
            securityContext.AllowedDomains.AddRange(_options.AuthDomains);
        }

        if (path.Contains("/api/"))
        {
            securityContext.CustomRequirements["X-API-Version"] = "1.0";
        }

        // Add CDN domains for asset paths
        if (path.Contains("/assets/") || path.Contains("/static/"))
        {
            securityContext.CdnDomains.AddRange(_options.AssetCdnDomains);
        }

        if (_options.WebRtcPaths.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            securityContext.CustomRequirements["allowWebRTC"] = true;
        }
    }

    private static bool IsApiRequest(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        return path.StartsWith("/api/") ||
               path.StartsWith("/graphql") ||
               context.Request.Headers.Accept.ToString().Contains("application/json");
    }

    private static bool IsStaticContent(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        return path.EndsWith(".css") ||
               path.EndsWith(".js") ||
               path.EndsWith(".png") ||
               path.EndsWith(".jpg") ||
               path.EndsWith(".jpeg") ||
               path.EndsWith(".gif") ||
               path.EndsWith(".svg") ||
               path.EndsWith(".ico") ||
               path.EndsWith(".woff") ||
               path.EndsWith(".woff2") ||
               path.EndsWith(".ttf") ||
               path.Contains("/assets/") ||
               path.Contains("/static/");
    }

    private bool WillHaveInlineScripts(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Check if path is likely to have inline scripts
        return !IsApiRequest(context) &&
               !IsStaticContent(context) &&
               (path.Contains("/admin") ||
                path.Contains("/dashboard") ||
                _options.InlineScriptPaths.Any(p => path.StartsWith(p.ToLowerInvariant())));
    }

    private bool WillHaveInlineStyles(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Check if path is likely to have inline styles
        return !IsApiRequest(context) &&
               !IsStaticContent(context) &&
               _options.InlineStylePaths.Any(p => path.StartsWith(p.ToLowerInvariant()));
    }

    private bool ShouldSkipSecurityHeaders(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";

        // Skip for excluded paths
        if (_options.ExcludedPaths.Any(excludedPath =>
            path.StartsWith(excludedPath.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Skip for health checks, metrics, etc.
        if (path.Contains("/health") ||
            path.Contains("/metrics") ||
            path.Contains("/swagger") ||
            path.Contains("/favicon"))
        {
            return true;
        }

        // Skip for OPTIONS requests
        if (context.Request.Method == "OPTIONS")
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Configuration options for security headers middleware
/// </summary>
public class SecurityHeadersMiddlewareOptions
{
    /// <summary>
    /// Enable security headers middleware
    /// </summary>
    public bool EnableSecurityHeaders { get; set; } = true;

    /// <summary>
    /// Apply minimal security headers on error
    /// </summary>
    public bool ApplyMinimalHeadersOnError { get; set; } = true;

    /// <summary>
    /// Log applied security headers
    /// </summary>
    public bool LogSecurityHeaders { get; set; } = false;

    /// <summary>
    /// Log CSP validation errors
    /// </summary>
    public bool LogCspValidationErrors { get; set; } = true;

    /// <summary>
    /// Analyze security headers after response
    /// </summary>
    public bool AnalyzeSecurityHeaders { get; set; } = true;

    /// <summary>
    /// Log security analysis to audit system
    /// </summary>
    public bool LogToAuditSystem { get; set; } = true;

    /// <summary>
    /// Detect inline content automatically
    /// </summary>
    public bool DetectInlineContent { get; set; } = true;

    /// <summary>
    /// Minimum CSP security score
    /// </summary>
    public int MinimumCspScore { get; set; } = 70;

    /// <summary>
    /// Minimum overall security score
    /// </summary>
    public int MinimumSecurityScore { get; set; } = 80;

    /// <summary>
    /// Paths to exclude from security headers
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics",
        "/swagger"
    };

    /// <summary>
    /// Path fragments whose requests get the WebRTC relaxation in the CSP.
    /// Only "/webrtc" is assumed: where else a real-time endpoint lives is a
    /// statement about the application's routes.
    /// </summary>
    public List<string> WebRtcPaths { get; set; } = new() { "/webrtc" };

    /// <summary>
    /// Globally allowed domains
    /// </summary>
    public List<string> AllowedDomains { get; set; } = new();

    /// <summary>
    /// CDN domains for assets
    /// </summary>
    public List<string> CdnDomains { get; set; } = new();

    /// <summary>
    /// Authentication-related domains
    /// </summary>
    public List<string> AuthDomains { get; set; } = new();

    /// <summary>
    /// Asset CDN domains
    /// </summary>
    public List<string> AssetCdnDomains { get; set; } = new();

    /// <summary>
    /// Paths that typically have inline scripts
    /// </summary>
    public List<string> InlineScriptPaths { get; set; } = new()
    {
        "/admin",
        "/dashboard"
    };

    /// <summary>
    /// Paths that typically have inline styles
    /// </summary>
    public List<string> InlineStylePaths { get; set; } = new();

    /// <summary>
    /// Custom security requirements
    /// </summary>
    public Dictionary<string, object?> CustomRequirements { get; set; } = new();
}

/// <summary>
/// Extension methods for security headers middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Use security headers middleware
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }

    /// <summary>
    /// Use security headers middleware with options
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(
        this IApplicationBuilder builder,
        Action<SecurityHeadersMiddlewareOptions> configureOptions)
    {
        var options = new SecurityHeadersMiddlewareOptions();
        configureOptions(options);

        return builder.UseMiddleware<SecurityHeadersMiddleware>(Options.Create(options));
    }
}