using Girder.Infrastructure.Security.Encryption;

namespace Girder.Infrastructure.Builder.Modules;

public static class EncryptionModule
{
    /// <summary>
    /// Add data encryption services with key management and background rotation.
    /// </summary>
    public static InfrastructureBuilder AddEncryption(this InfrastructureBuilder builder)
    {
        builder.EncryptionEnabled = true;

        builder.RequiresProvider<Girder.Abstractions.Security.Encryption.IDataEncryptionService>(
            "AddEncryption()", "AddRedisEncryption() from Girder.Redis");
        builder.RequiresProvider<Girder.Abstractions.Security.Encryption.IMasterKeyProvider>(
            "AddEncryption()", "AddConfiguredMasterKey() or AddSecretStoreMasterKey()");

        builder.Services.AddDataEncryption(builder.Configuration);
        return builder;
    }
}
