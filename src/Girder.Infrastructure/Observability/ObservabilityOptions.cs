namespace Girder.Infrastructure.Observability;

/// <summary>
/// Runtime observability and logging controls shared across all .NET services.
/// Environment variables map via the section name, e.g.
/// Observability__CaptureDatabaseStatements=false
/// </summary>
/// <remarks>
/// There is no switch for exporters. Girder emits OTLP — the neutral protocol —
/// and any backend-specific exporter is added by the application through the
/// <c>configure</c> callback of AddTracing/AddMetrics (ADR-0001).
/// </remarks>
public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool CaptureDatabaseStatements { get; set; } = false;

    public bool EnableDetailedHttpLogging { get; set; } = false;

    public double SlowRequestLogThresholdMs { get; set; } = 1000;
}
