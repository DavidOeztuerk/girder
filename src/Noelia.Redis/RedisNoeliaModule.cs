using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Encryption;
using Noelia.Redis.Security;
using StackExchange.Redis;

namespace Noelia.Redis;

/// <summary>Exposes Redis-backed encryption in the Noelia composition.</summary>
public static class RedisNoeliaModule
{
    public static NoeliaModule Encryption => new("Redis.Encryption");

    public static NoeliaBuilder UseRedisEncryption(this NoeliaBuilder noelia)
    {
        ArgumentNullException.ThrowIfNull(noelia);

        return noelia.Use(
            Encryption,
            builder => builder.Services.AddRedisEncryption(),
            contract => contract
                .Requires<IConnectionMultiplexer>(new NoeliaProviderHint(
                    "Noelia.Redis", "AddRedisConnection(connectionString, instanceName)"))
                .Requires<IMasterKeyProvider>(new NoeliaProviderHint(
                    "Noelia.Infrastructure", "AddConfiguredMasterKey() or AddSecretStoreMasterKey()"))
                .Provides<IDataEncryptionService>(
                    "Noelia.Redis", "UseRedisEncryption()"));
    }
}
