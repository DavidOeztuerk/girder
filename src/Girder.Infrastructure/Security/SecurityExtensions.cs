using Girder.Abstractions.Security.Encryption;
using Girder.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security;

/// <summary>
/// Secret management, rotation and audit logging.
/// </summary>
/// <remarks>
/// JWT authentication, token revocation and the general security setup live in
/// <c>Girder.Infrastructure.Extensions.ServiceCollectionExtensions</c>.
/// <para>
/// Reached through <c>AddSharedInfrastructure</c> rather than called directly:
/// the modules depend on registration order that the builder establishes.
/// </para>
/// </remarks>
public static class SecurityExtensions
{


    /// <summary>
    /// Add secure secret management
    /// </summary>
    public static IServiceCollection AddSecretManagement(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // The ISecretManager implementation comes from a provider package —
        // AddRedisSecretManager() or AddInMemorySecretManager(). Girder does not
        // pick one, because picking one is picking a server.

        // Configure secret rotation
        var rotationConfig = configuration.GetSection("SecretRotation");
        services.Configure<SecretRotationOptions>(rotationConfig);

        // Add secret rotation background service

        return services;
    }

    /// <summary>
    /// Add security audit logging
    /// </summary>
    public static IServiceCollection AddSecurityAuditLogging(
        this IServiceCollection services)
    {
        services.AddSingleton<ISecurityAuditLogger, SecurityAuditLogger>();
        // SecurityAuditMiddleware should not be registered as a service
        // It will be used directly via UseMiddleware<SecurityAuditMiddleware>()
        
        return services;
    }

}

/// <summary>
/// Secret rotation configuration options
/// </summary>
public class SecretRotationOptions
{
    /// <summary>
    /// Enable automatic secret rotation
    /// </summary>
    public bool EnableRotation { get; set; } = true;

    /// <summary>
    /// Rotation interval in hours
    /// </summary>
    public int RotationIntervalHours { get; set; } = 24 * 7; // Weekly

    /// <summary>
    /// Number of old secrets to keep
    /// </summary>
    public int KeepOldSecretsCount { get; set; } = 3;

    /// <summary>
    /// Secrets to rotate
    /// </summary>
    public List<string> SecretsToRotate { get; set; } = new()
    {
        "JwtSecret",
        "EncryptionKey"
    };
}