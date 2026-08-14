using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Security;

/// <summary>
/// Security-specific extension methods
///
/// IMPORTANT: This file contains ONLY security-specific features:
/// - Secret Management & Rotation
/// - Security Audit Logging
///
/// For JWT Authentication, Token Revocation, and general security setup,
/// see Infrastructure/Extensions/ServiceCollectionExtensions.cs
///
/// These methods are called from AddSharedInfrastructure().
/// Do NOT call them directly from service Program.cs files.
/// </summary>
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
        // Register secret manager with factory to inject IHostEnvironment
        services.AddSingleton<ISecretManager>(provider =>
        {
            var connectionMultiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
            var logger = provider.GetRequiredService<ILogger<SecretManager>>();
            return new SecretManager(connectionMultiplexer, configuration, environment, logger);
        });

        // Configure secret rotation
        var rotationConfig = configuration.GetSection("SecretRotation");
        services.Configure<SecretRotationOptions>(rotationConfig);

        // Add secret rotation background service
        services.AddHostedService<SecretRotationService>();

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