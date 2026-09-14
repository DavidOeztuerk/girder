using Noelia.Abstractions.Security;
using Noelia.Abstractions.Security.Secrets;
using Noelia.Application.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Noelia.Infrastructure.Security;

/// <summary>
/// Secret management, rotation and audit logging.
/// </summary>
/// <remarks>
/// JWT authentication, token revocation and the general security setup live in
/// <c>Noelia.Infrastructure.Extensions.ServiceCollectionExtensions</c>.
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
    public static IServiceCollection AddSecretManagement(this IServiceCollection services)
    {
        // Choosing an implementation chooses where secrets live. Noelia makes
        // that operator decision explicit and validates it before serving.
        services.RequiresProvider<ISecretProvider>(
            "AddSecretManagement()",
            "AddOpenBaoSecretProvider(configuration), AddRedisSecretProvider(...), "
            + "AddInMemorySecretProvider(), or register ISecretProvider");

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
