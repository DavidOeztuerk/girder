using Noelia.Infrastructure.Communication;
using Noelia.Infrastructure.HealthChecks;

namespace Noelia.Infrastructure.Builder.Modules;

public static class CommunicationModule
{
    /// <summary>
    /// Add inter-service communication via ServiceCommunicationManager.
    /// A service that calls no peers simply does not call this.
    /// </summary>
    public static InfrastructureBuilder AddCommunication(this InfrastructureBuilder builder)
    {
        builder.CommunicationEnabled = true;

        builder.RequiresProvider<Noelia.Abstractions.Messaging.IEventBus>(
            "AddCommunication()", "AddMessaging(configuration, assemblies) from Noelia.Messaging.MassTransit");
        builder.RequiresProvider<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(
            "AddCommunication()", "AddRedisConnection(...) or services.AddDistributedMemoryCache()");

        builder.Services.AddComprehensiveHealthChecks(builder.Configuration);
        builder.Services.AddServiceCommunication(builder.Configuration);

        return builder;
    }
}
