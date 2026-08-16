using Girder.Infrastructure.Security.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

public static class SecurityMonitoringModule
{
    /// <summary>
    /// Add security monitoring — alerting and threat signals.
    /// </summary>
    public static InfrastructureBuilder AddSecurityMonitoring(this InfrastructureBuilder builder)
    {
        builder.SecurityMonitoringEnabled = true;

        builder.Services.Configure<SecurityAlertConfiguration>(
            builder.Configuration.GetSection("SecurityAlerts"));
        builder.Services.AddSingleton<ISecurityAlertService, SecurityAlertService>();
        builder.Services.AddHttpContextAccessor();

        return builder;
    }
}
