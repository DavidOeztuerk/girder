using Girder.Infrastructure.BackgroundServices;
using Girder.Infrastructure.Security.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

public static class SecurityMonitoringModule
{
    /// <summary>
    /// Add security monitoring — SecurityAlertService + ThreatDetectionBackgroundService.
    /// </summary>
    public static InfrastructureBuilder AddSecurityMonitoring(this InfrastructureBuilder builder)
    {
        builder.SecurityMonitoringEnabled = true;

        builder.Services.Configure<SecurityAlertConfiguration>(
            builder.Configuration.GetSection("SecurityAlerts"));
        builder.Services.AddSingleton<ISecurityAlertService, SecurityAlertService>();
        builder.Services.AddHostedService<ThreatDetectionBackgroundService>();
        builder.Services.AddHttpContextAccessor();

        return builder;
    }
}
