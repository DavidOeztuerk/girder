using Girder.Infrastructure.Communication;
using Girder.Infrastructure.HealthChecks;

namespace Girder.Infrastructure.Builder.Modules;

public static class CommunicationModule
{
    /// <summary>
    /// Add inter-service communication via ServiceCommunicationManager.
    /// A service that calls no peers simply does not call this.
    /// </summary>
    public static InfrastructureBuilder AddCommunication(this InfrastructureBuilder builder)
    {
        builder.CommunicationEnabled = true;

        builder.Services.AddComprehensiveHealthChecks(builder.Configuration);
        builder.Services.AddServiceCommunication(builder.Configuration);

        return builder;
    }
}
