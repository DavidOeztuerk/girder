using Infrastructure.Security;

namespace Infrastructure.Builder.Modules;

public static class SecretManagementModule
{
    /// <summary>
    /// Add secret management and rotation services.
    /// </summary>
    public static InfrastructureBuilder AddSecretManagement(this InfrastructureBuilder builder)
    {
        builder.SecretManagementEnabled = true;
        builder.Services.AddSecretManagement(builder.Configuration, builder.Environment);
        return builder;
    }
}
