using Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Builder.Modules;

public static class ObservabilityModule
{
    /// <summary>
    /// Add telemetry (OpenTelemetry tracing + metrics) and performance monitoring.
    /// </summary>
    public static InfrastructureBuilder AddObservability(this InfrastructureBuilder builder)
    {
        builder.ObservabilityEnabled = true;

        var observabilityOptions =
            builder.Configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
            ?? new ObservabilityOptions();

        builder.Services.Configure<ObservabilityOptions>(
            builder.Configuration.GetSection(ObservabilityOptions.SectionName));

        builder.Services
            .AddTelemetry(builder.ServiceName, "1.0.0", b => b
                .ConfigureObservability(observabilityOptions)
                .AddTracing()
                .AddMetrics());

        builder.Services.AddSingleton<IPerformanceMetrics, PerformanceMetrics>();
        builder.Services.AddSingleton<IPerformanceMonitoringService, PerformanceMonitoringService>();

        return builder;
    }
}
