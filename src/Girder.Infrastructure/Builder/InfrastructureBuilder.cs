using Girder.Abstractions.Hosting;
using Girder.Application.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Builder;

public class InfrastructureBuilder
{
    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    public IHostEnvironment Environment { get; }
    public string ServiceName { get; }

    // Module activation tracking
    internal bool JwtEnabled { get; set; }
    internal bool SecretManagementEnabled { get; set; }
    internal bool EncryptionEnabled { get; set; }
    internal bool RateLimitingEnabled { get; set; }
    internal bool CachingEnabled { get; set; }
    internal bool ResilienceEnabled { get; set; }
    internal bool HealthChecksEnabled { get; set; }
    internal bool SecurityHeadersEnabled { get; set; }
    internal bool InputSanitizationEnabled { get; set; }
    internal bool AuditEnabled { get; set; }
    internal bool CommunicationEnabled { get; set; }
    internal bool ObservabilityEnabled { get; set; }
    internal bool AuthorizationEnabled { get; set; }
    internal bool SecurityMonitoringEnabled { get; set; }

    // Redis state (resolved once, shared across modules)
    internal string? RedisConnectionString { get; set; }

    /// <summary>
    /// Declares that a module needs a service it does not register itself.
    /// Checked at startup, not at registration: the provider is usually added
    /// after this builder has run.
    /// </summary>
    /// <param name="requiredBy">The module, as the caller writes it.</param>
    /// <param name="remedy">The call or calls that satisfy the requirement.</param>
    public InfrastructureBuilder RequiresProvider<TService>(string requiredBy, string remedy)
        where TService : class
    {
        Services.RequiresProvider<TService>(requiredBy, remedy);
        return this;
    }

    public InfrastructureBuilder(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        Services = services;

        // Ask for the collector even when no module declares anything: the
        // requirements are shared with whatever else registers into this
        // collection, and a second instance would shadow the first.
        services.GetProviderRequirements();

        Configuration = configuration;
        Environment = environment;
        ServiceName = serviceName;

        RedisConnectionString = ResolveRedisConnectionString(configuration);
    }

    private static string? ResolveRedisConnectionString(IConfiguration config)
    {
        var fromConfig = config.GetConnectionString("Redis");
        var fromConfigAlt = config["Redis:ConnectionString"];
        var fromEnv = System.Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

        return !string.IsNullOrEmpty(fromConfig) ? fromConfig
            : !string.IsNullOrEmpty(fromConfigAlt) ? fromConfigAlt
            : !string.IsNullOrEmpty(fromEnv) ? fromEnv
            : null;
    }
}
