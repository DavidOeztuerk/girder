using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Girder.Redis;

public static class RedisConnectionRegistration
{
    /// <summary>
    /// Connects to a RESP server — Redis, Valkey, Garnet or KeyDB — and shares
    /// one multiplexer with every Girder.Redis component.
    /// </summary>
    /// <param name="services">The container to register in.</param>
    /// <param name="connectionString">Address of the RESP server.</param>
    /// <param name="instanceName">
    /// Prefixes cache keys so services sharing one server do not read each
    /// other's entries.
    /// </param>
    /// <remarks>
    /// Connects eagerly so a wrong address fails at startup rather than on the
    /// first request. <c>AbortOnConnectFail</c> is off, so a server that is
    /// merely slow to come up does not stop the process.
    /// <para>
    /// Admin commands (FLUSHALL, CONFIG, DEBUG) are enabled only when
    /// ASPNETCORE_ENVIRONMENT is Development.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRedisConnection(
        this IServiceCollection services,
        string connectionString,
        string instanceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);

        var options = ConfigurationOptions.Parse(connectionString);
        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;
        options.AsyncTimeout = 5000;
        options.ConnectRetry = 3;
        options.AbortOnConnectFail = false;
        options.KeepAlive = 60;

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        options.AllowAdmin = string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase);

        var multiplexer = ConnectionMultiplexer.Connect(options);

        services.AddSingleton<IConnectionMultiplexer>(multiplexer);
        services.AddStackExchangeRedisCache(cache =>
        {
            cache.ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);
            cache.InstanceName = instanceName.ToLowerInvariant() + ":";
        });

        return services;
    }
}
