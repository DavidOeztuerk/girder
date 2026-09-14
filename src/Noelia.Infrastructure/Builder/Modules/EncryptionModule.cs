using Noelia.Infrastructure.Security.Encryption;

namespace Noelia.Infrastructure.Builder.Modules;

public static class EncryptionModule
{
    /// <summary>
    /// Add data encryption services with key management and background rotation.
    /// </summary>
    public static InfrastructureBuilder AddEncryption(this InfrastructureBuilder builder)
    {
        builder.EncryptionEnabled = true;

        builder.RequiresProvider<Noelia.Abstractions.Security.Encryption.IDataEncryptionService>(
            "AddEncryption()", "AddRedisEncryption() from Noelia.Redis");
        builder.RequiresProvider<Noelia.Abstractions.Security.Encryption.IMasterKeyProvider>(
            "AddEncryption()", "AddConfiguredMasterKey() or AddSecretStoreMasterKey()");

        builder.Services.AddDataEncryption(builder.Configuration);
        return builder;
    }
}
