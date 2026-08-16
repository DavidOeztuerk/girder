using Girder.Abstractions.Security.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Girder.Infrastructure.Security.Audit;

/// <summary>
/// Extension methods for security audit services
/// </summary>
public static class SecurityAuditExtensions
{
  /// <summary>
  /// Add security audit services
  /// </summary>
  public static IServiceCollection AddSecurityAudit(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // Check environment variable as fallback
    var redisConnectionString = configuration.GetConnectionString("Redis")
        ?? configuration["Redis:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

    if (!string.IsNullOrEmpty(redisConnectionString))
    {
      // Redis-based audit service
    }
    else
    {
      // In-memory fallback
    }

    // Add audit middleware
    services.AddTransient<SecurityAuditMiddleware>();

    // Add background services

    // Configure audit options
    services.Configure<SecurityAuditOptions>(configuration.GetSection("SecurityAudit"));

    return services;
  }

  /// <summary>
  /// Add security audit middleware
  /// </summary>
  public static IServiceCollection AddSecurityAuditMiddleware(this IServiceCollection services)
  {
    services.AddTransient<SecurityAuditMiddleware>();
    return services;
  }
}

/// <summary>
/// Security audit configuration options
/// </summary>
public class SecurityAuditOptions
{
  /// <summary>
  /// Enable audit logging
  /// </summary>
  public bool EnableAuditLogging { get; set; } = true;

  /// <summary>
  /// Default retention period in days
  /// </summary>
  public int DefaultRetentionDays { get; set; } = 2555; // ~7 years

  /// <summary>
  /// Archive old logs after this many days
  /// </summary>
  public int ArchiveAfterDays { get; set; } = 365; // 1 year

  /// <summary>
  /// Maximum number of events to store
  /// </summary>
  public int MaxEvents { get; set; } = 1000000;

  /// <summary>
  /// Signing key for tamper detection (Base64 encoded)
  /// </summary>
  public string? SigningKey { get; set; }

  /// <summary>
  /// Enable integrity verification
  /// </summary>
  public bool EnableIntegrityVerification { get; set; } = true;

  /// <summary>
  /// Verify integrity every X hours
  /// </summary>
  public int IntegrityVerificationIntervalHours { get; set; } = 24;

  /// <summary>
  /// Log all requests or only sensitive ones
  /// </summary>
  public bool LogAllRequests { get; set; } = false;

  /// <summary>
  /// Include request/response bodies in logs
  /// </summary>
  public bool IncludeRequestBodies { get; set; } = true;

  /// <summary>
  /// Maximum request body size to log (bytes)
  /// </summary>
  public int MaxRequestBodySize { get; set; } = 1024 * 1024; // 1MB

  /// <summary>
  /// Compliance requirements
  /// </summary>
  public List<string> ComplianceRequirements { get; set; } = new() { "GDPR" };

  /// <summary>
  /// Export formats to support
  /// </summary>
  public List<string> SupportedExportFormats { get; set; } = new() { "JSON", "CSV", "XML" };
}
