namespace Girder.Infrastructure.Observability;

/// <summary>
/// Runtime observability and logging controls shared across all .NET services.
/// Environment variables map via the section name, e.g.
/// Observability__EnableConsoleExporter=false
/// </summary>
public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool EnableConsoleExporter { get; set; } = false;

    public bool CaptureDatabaseStatements { get; set; } = false;

    public bool EnableDetailedHttpLogging { get; set; } = false;

    public double SlowRequestLogThresholdMs { get; set; } = 1000;
}
