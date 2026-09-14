namespace Noelia.Abstractions.Sovereignty;

/// <summary>
/// Reports only the hosts and jurisdictions derived from configuration; it
/// never exposes a connection string or credential.
/// </summary>
public interface ISovereigntyReport
{
    SovereigntyAssessment Assess();
}
