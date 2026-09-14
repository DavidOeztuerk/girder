using Noelia.Infrastructure.Security;

namespace Noelia.Infrastructure.Builder.Modules;

public static class SecretManagementModule
{
    /// <summary>
    /// Add secret management and rotation services.
    /// </summary>
    public static InfrastructureBuilder AddSecretManagement(this InfrastructureBuilder builder)
    {
        builder.SecretManagementEnabled = true;
        builder.Services.AddSecretManagement();
        return builder;
    }
}
