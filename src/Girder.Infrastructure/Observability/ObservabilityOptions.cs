namespace Girder.Infrastructure.Observability;

/// <summary>
/// Runtime tracing, metrics and logging controls, bound from the
/// <see cref="SectionName"/> configuration section. Environment variables map
/// with a double underscore, e.g. <c>Observability__CaptureDatabaseStatements=true</c>.
/// </summary>
/// <remarks>
/// Exporters are not configured here. Girder emits OTLP; to send telemetry
/// anywhere else, add the exporter through the <c>configure</c> callback of
/// <c>TelemetryBuilder.AddTracing</c> or <c>AddMetrics</c>.
/// </remarks>
public class ObservabilityOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "Observability";

    /// <summary>
    /// Records SQL text on database spans. Off by default: statements can
    /// contain personal data and parameter values.
    /// </summary>
    public bool CaptureDatabaseStatements { get; set; } = false;

    /// <summary>Logs request and response details for every HTTP call.</summary>
    public bool EnableDetailedHttpLogging { get; set; } = false;

    /// <summary>
    /// Requests slower than this are logged as warnings rather than information.
    /// </summary>
    public double SlowRequestLogThresholdMs { get; set; } = 1000;
}
