using Noelia.Abstractions.Security.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Noelia.Infrastructure.Security.Audit;

/// <summary>
/// Extension methods for security audit services
/// </summary>
public static class SecurityAuditExtensions
{
  /// <summary>
  /// Add security audit services
  /// </summary>
  public static IServiceCollection AddSecurityAudit(this IServiceCollection services)
  {
    // The ISecurityAuditService implementation comes from a provider package —
    // AddRedisSecurityAudit() or AddInMemorySecurityAudit().

    // Add audit middleware
    services.AddTransient<SecurityAuditMiddleware>();

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
