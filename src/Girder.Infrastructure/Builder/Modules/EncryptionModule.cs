using Infrastructure.Security.Encryption;

namespace Infrastructure.Builder.Modules;

public static class EncryptionModule
{
    /// <summary>
    /// Add data encryption services with key management and background rotation.
    /// </summary>
    public static InfrastructureBuilder AddEncryption(this InfrastructureBuilder builder)
    {
        builder.EncryptionEnabled = true;
        builder.Services.AddDataEncryption(builder.Configuration);
        return builder;
    }
}
