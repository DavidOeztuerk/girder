using Girder.Infrastructure.Communication;
using Girder.Infrastructure.HealthChecks;

namespace Girder.Infrastructure.Builder.Modules;

public static class CommunicationModule
{
    /// <summary>
    /// Add inter-service communication via ServiceCommunicationManager.
    /// Skipped for Gateway.
    /// </summary>
    public static InfrastructureBuilder AddCommunication(this InfrastructureBuilder builder)
    {
        builder.CommunicationEnabled = true;

        if (!builder.ServiceName.Equals("Gateway", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddComprehensiveHealthChecks(builder.Configuration);
            builder.Services.AddServiceCommunication(builder.Configuration);
        }

        return builder;
    }
}
