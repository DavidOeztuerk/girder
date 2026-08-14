using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security.Headers;

/// <summary>
/// Extension methods for configuring security headers services
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Add security headers services
    /// </summary>
    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register security headers service
        services.AddSingleton<ISecurityHeadersService, SecurityHeadersService>();
        
        // Configure security headers options
        services.Configure<SecurityHeadersOptions>(configuration.GetSection("SecurityHeaders"));
        
        // Configure middleware options
        services.Configure<SecurityHeadersMiddlewareOptions>(configuration.GetSection("SecurityHeadersMiddleware"));

        return services;
    }

    /// <summary>
    /// Add security headers services with custom configuration
    /// </summary>
    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        Action<SecurityHeadersOptions> configureHeaders,
        Action<SecurityHeadersMiddlewareOptions>? configureMiddleware = null)
    {
        // Register security headers service
        services.AddSingleton<ISecurityHeadersService, SecurityHeadersService>();
        
        // Configure options
        services.Configure(configureHeaders);
        
        if (configureMiddleware != null)
        {
            services.Configure(configureMiddleware);
        }

        return services;
    }

    /// <summary>
    /// Add security headers with fluent configuration
    /// </summary>
    public static IServiceCollection AddSecurityHeaders(
        this IServiceCollection services,
        Action<ISecurityHeadersBuilder> configure)
    {
        var builder = new SecurityHeadersBuilder(services);
        configure(builder);
        
        return services;
    }
}

/// <summary>
/// Builder interface for configuring security headers
/// </summary>
public interface ISecurityHeadersBuilder
{
    /// <summary>
    /// Configure Content Security Policy
    /// </summary>
    ISecurityHeadersBuilder ConfigureContentSecurityPolicy(Action<ContentSecurityPolicyBuilder> configure);

    /// <summary>
    /// Configure HSTS (HTTP Strict Transport Security)
    /// </summary>
    ISecurityHeadersBuilder ConfigureHsts(int maxAge = 31536000, bool includeSubDomains = true, bool preload = false);

    /// <summary>
    /// Configure allowed domains
    /// </summary>
    ISecurityHeadersBuilder AddAllowedDomains(params string[] domains);

    /// <summary>
    /// Configure CDN domains
    /// </summary>
    ISecurityHeadersBuilder AddCdnDomains(params string[] domains);

    /// <summary>
    /// Enable or disable specific security features
    /// </summary>
    ISecurityHeadersBuilder EnablePermissionsPolicy(bool enabled = true);
    ISecurityHeadersBuilder EnableCrossOriginEmbedderPolicy(bool enabled = true);
    ISecurityHeadersBuilder EnableCrossOriginOpenerPolicy(bool enabled = true);
    ISecurityHeadersBuilder EnableCrossOriginResourcePolicy(bool enabled = true);

    /// <summary>
    /// Configure CSP reporting
    /// </summary>
    ISecurityHeadersBuilder ConfigureReporting(string reportUri);

    /// <summary>
    /// Configure middleware options
    /// </summary>
    ISecurityHeadersBuilder ConfigureMiddleware(Action<SecurityHeadersMiddlewareOptions> configure);

    /// <summary>
    /// Add custom security requirement
    /// </summary>
    ISecurityHeadersBuilder AddCustomRequirement(string key, object? value);

    /// <summary>
    /// Configure for development environment
    /// </summary>
    ISecurityHeadersBuilder ForDevelopment();

    /// <summary>
    /// Configure for production environment
    /// </summary>
    ISecurityHeadersBuilder ForProduction();
}

/// <summary>
/// Implementation of security headers builder
/// </summary>
public class SecurityHeadersBuilder : ISecurityHeadersBuilder
{
    private readonly IServiceCollection _services;
    private readonly SecurityHeadersOptions _headerOptions;
    private readonly SecurityHeadersMiddlewareOptions _middlewareOptions;

    public SecurityHeadersBuilder(IServiceCollection services)
    {
        _services = services;
        _headerOptions = new SecurityHeadersOptions();
        _middlewareOptions = new SecurityHeadersMiddlewareOptions();
        
        // Register services
        _services.AddSingleton<ISecurityHeadersService, SecurityHeadersService>();
        _services.Configure<SecurityHeadersOptions>(options => CopyOptions(_headerOptions, options));
        _services.Configure<SecurityHeadersMiddlewareOptions>(options => CopyOptions(_middlewareOptions, options));
    }

    public ISecurityHeadersBuilder ConfigureContentSecurityPolicy(Action<ContentSecurityPolicyBuilder> configure)
    {
        var cspBuilder = new ContentSecurityPolicyBuilder();
        configure(cspBuilder);
        
        // Store CSP configuration for later use
        _headerOptions.EnableDefaultCsp = true;
        
        return this;
    }

    public ISecurityHeadersBuilder ConfigureHsts(int maxAge = 31536000, bool includeSubDomains = true, bool preload = false)
    {
        _headerOptions.EnableHsts = true;
        _headerOptions.HstsMaxAge = maxAge;
        _headerOptions.HstsIncludeSubDomains = includeSubDomains;
        _headerOptions.HstsPreload = preload;
        
        return this;
    }

    public ISecurityHeadersBuilder AddAllowedDomains(params string[] domains)
    {
        _middlewareOptions.AllowedDomains.AddRange(domains);
        return this;
    }

    public ISecurityHeadersBuilder AddCdnDomains(params string[] domains)
    {
        _middlewareOptions.CdnDomains.AddRange(domains);
        return this;
    }

    public ISecurityHeadersBuilder EnablePermissionsPolicy(bool enabled = true)
    {
        _headerOptions.EnablePermissionsPolicy = enabled;
        return this;
    }

    public ISecurityHeadersBuilder EnableCrossOriginEmbedderPolicy(bool enabled = true)
    {
        _headerOptions.EnableCrossOriginEmbedderPolicy = enabled;
        return this;
    }

    public ISecurityHeadersBuilder EnableCrossOriginOpenerPolicy(bool enabled = true)
    {
        _headerOptions.EnableCrossOriginOpenerPolicy = enabled;
        return this;
    }

    public ISecurityHeadersBuilder EnableCrossOriginResourcePolicy(bool enabled = true)
    {
        _headerOptions.EnableCrossOriginResourcePolicy = enabled;
        return this;
    }

    public ISecurityHeadersBuilder ConfigureReporting(string reportUri)
    {
        _headerOptions.ReportUri = reportUri;
        return this;
    }

    public ISecurityHeadersBuilder ConfigureMiddleware(Action<SecurityHeadersMiddlewareOptions> configure)
    {
        configure(_middlewareOptions);
        return this;
    }

    public ISecurityHeadersBuilder AddCustomRequirement(string key, object? value)
    {
        _middlewareOptions.CustomRequirements[key] = value;
        return this;
    }

    public ISecurityHeadersBuilder ForDevelopment()
    {
        // Development-friendly settings
        _headerOptions.EnableDefaultCsp = false; // Less restrictive CSP
        _headerOptions.EnableHsts = false; // No HSTS in development
        _middlewareOptions.LogSecurityHeaders = true;
        _middlewareOptions.LogCspValidationErrors = true;
        _middlewareOptions.AnalyzeSecurityHeaders = true;
        _middlewareOptions.MinimumCspScore = 50; // Lower requirements
        _middlewareOptions.MinimumSecurityScore = 60;
        
        // Allow localhost domains
        _middlewareOptions.AllowedDomains.AddRange(new[]
        {
            "localhost:*",
            "127.0.0.1:*",
            "*.localhost"
        });

        return this;
    }

    public ISecurityHeadersBuilder ForProduction()
    {
        // Production-grade settings
        _headerOptions.EnableDefaultCsp = true;
        _headerOptions.EnableHsts = true;
        _headerOptions.HstsMaxAge = 31536000; // 1 year
        _headerOptions.HstsIncludeSubDomains = true;
        _headerOptions.HstsPreload = true;
        _headerOptions.EnablePermissionsPolicy = true;
        _headerOptions.EnableCrossOriginEmbedderPolicy = true;
        _headerOptions.EnableCrossOriginOpenerPolicy = true;
        _headerOptions.EnableCrossOriginResourcePolicy = true;
        _headerOptions.UpgradeInsecureRequests = true;
        
        _middlewareOptions.LogSecurityHeaders = false; // Reduce logging overhead
        _middlewareOptions.AnalyzeSecurityHeaders = true;
        _middlewareOptions.LogToAuditSystem = true;
        _middlewareOptions.MinimumCspScore = 80; // Strict requirements
        _middlewareOptions.MinimumSecurityScore = 85;

        return this;
    }

    private static void CopyOptions<T>(T source, T destination)
    {
        var properties = typeof(T).GetProperties();
        foreach (var property in properties)
        {
            if (property.CanRead && property.CanWrite)
            {
                var value = property.GetValue(source);
                property.SetValue(destination, value);
            }
        }
    }
}

/// <summary>
/// Attribute to mark controllers/actions for custom CSP
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ContentSecurityPolicyAttribute : Attribute
{
    public string? ScriptSources { get; set; }
    public string? StyleSources { get; set; }
    public string? ImageSources { get; set; }
    public string? ConnectSources { get; set; }
    public bool AllowUnsafeInline { get; set; }
    public bool AllowUnsafeEval { get; set; }
    public bool ReportOnly { get; set; }
}

/// <summary>
/// Helper extensions for HttpContext
/// </summary>
public static class HttpContextSecurityExtensions
{
    /// <summary>
    /// Get script nonce for the current request
    /// </summary>
    public static string? GetScriptNonce(this HttpContext context)
    {
        return context.Items["ScriptNonce"] as string;
    }

    /// <summary>
    /// Get style nonce for the current request
    /// </summary>
    public static string? GetStyleNonce(this HttpContext context)
    {
        return context.Items["StyleNonce"] as string;
    }

    /// <summary>
    /// Add allowed domain for current request
    /// </summary>
    public static void AddAllowedDomain(this HttpContext context, string domain)
    {
        var key = "SecurityHeaders_AllowedDomains";
        if (context.Items[key] is not List<string> domains)
        {
            domains = new List<string>();
            context.Items[key] = domains;
        }
        
        if (!domains.Contains(domain))
        {
            domains.Add(domain);
        }
    }

    /// <summary>
    /// Get allowed domains for current request
    /// </summary>
    public static List<string> GetAllowedDomains(this HttpContext context)
    {
        var key = "SecurityHeaders_AllowedDomains";
        return context.Items[key] as List<string> ?? new List<string>();
    }
}