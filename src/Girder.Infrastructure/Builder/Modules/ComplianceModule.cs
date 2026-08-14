using Microsoft.Extensions.Configuration;
using Girder.Infrastructure.Security.Compliance;

namespace Girder.Infrastructure.Builder.Modules;

public static class ComplianceModule
{
    /// <summary>
    /// Enable GDPR/DSGVO compliance services via configuration binding.
    /// Registers DataProtectionService (implements IDataProtectionService, IConsentManagementService,
    /// IDataBreachNotificationService) plus three background services.
    /// </summary>
    public static InfrastructureBuilder AddCompliance(this InfrastructureBuilder builder)
    {
        builder.ComplianceEnabled = true;
        builder.Services.AddDataProtectionCompliance(builder.Configuration);
        return builder;
    }

    /// <summary>
    /// Enable compliance with fluent builder configuration.
    /// </summary>
    public static InfrastructureBuilder AddCompliance(
        this InfrastructureBuilder builder,
        Action<IComplianceBuilder> configure)
    {
        builder.ComplianceEnabled = true;
        builder.Services.AddDataProtectionCompliance(configure);
        return builder;
    }
}
