using Girder.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Girder.Redis.Security;

public static class RedisSecretRegistration
{
    /// <summary>
    /// Stores secrets in Redis, encrypted at rest.
    /// </summary>
    /// <remarks>
    /// Requires an <see cref="IConnectionMultiplexer"/> in the container.
    /// Secrets held in a cache server are only as durable as that server's
    /// persistence settings — for anything that cannot be regenerated, prefer a
    /// secret store through <c>ISecretProvider</c>.
    /// </remarks>
    public static IServiceCollection AddRedisSecretManager(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment) =>
        services.AddSingleton<ISecretManager>(provider => new SecretManager(
            provider.GetRequiredService<IConnectionMultiplexer>(),
            configuration,
            environment,
            provider.GetRequiredService<ILogger<SecretManager>>()));
}
