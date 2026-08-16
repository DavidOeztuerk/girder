using Girder.Abstractions.Security.Encryption;
using Girder.Redis.Security.Encryption;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Redis.Security;

public static class RedisEncryptionRegistration
{
    /// <summary>
    /// Keeps encryption keys and their metadata in Redis.
    /// </summary>
    /// <remarks>
    /// Requires an <see cref="StackExchange.Redis.IConnectionMultiplexer"/> and
    /// an <see cref="IMasterKeyProvider"/> — Girder never generates a master
    /// key, because a key it invented would be a key nobody chose to trust.
    /// <para>
    /// Keys live only as long as the server persists them. Configure Redis
    /// persistence accordingly, or hold the master key in a secret store and
    /// treat this as a cache of derived material.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRedisEncryption(this IServiceCollection services)
    {
        services.AddSingleton<IKeyManagementService, KeyManagementService>();
        services.AddSingleton<IDataEncryptionService, DataEncryptionService>();

        return services;
    }
}
