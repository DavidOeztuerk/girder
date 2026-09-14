using Noelia.Abstractions.Hosting;

namespace Noelia.Abstractions.Security.Checks;

/// <summary>The stable outcome vocabulary for every Noelia security check.</summary>
public enum SecurityCheckStatus
{
    Pass,
    Warning,
    Fail,
    NotApplicable
}

/// <summary>How urgently an operator should act on a non-passing result.</summary>
public enum SecurityCheckSeverity
{
    Informational,
    Low,
    Medium,
    High,
    Critical
}

/// <summary>The security boundary a check examines.</summary>
public enum SecurityCheckCategory
{
    Composition,
    Authentication,
    Browser,
    Session,
    Secrets,
    Encryption,
    AbusePrevention,
    Revocation
}

/// <summary>A value-free security finding safe to show in operator tooling.</summary>
/// <remarks>
/// Summary and remediation describe shapes and actions only. Implementations
/// must never copy configuration values, keys, tokens, connection strings or
/// raw exception messages into either field.
/// </remarks>
public sealed record SecurityCheckResult(
    string Id,
    NoeliaModule Module,
    SecurityCheckCategory Category,
    SecurityCheckStatus Status,
    SecurityCheckSeverity Severity,
    string Summary,
    string Remediation);

/// <summary>One explicitly executable security assertion.</summary>
public interface ISecurityCheck
{
    string Id { get; }
    NoeliaModule Module { get; }
    SecurityCheckCategory Category { get; }
    SecurityCheckSeverity Severity { get; }
    string Remediation { get; }

    Task<SecurityCheckResult> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>Runs checks for modules in the actual composition.</summary>
public interface ISecurityCheckRunner
{
    Task<IReadOnlyList<SecurityCheckResult>> RunAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>The latest completed startup or operator-invoked run.</summary>
public interface ISecurityCheckReport
{
    IReadOnlyList<SecurityCheckResult> Latest { get; }
}

/// <summary>Execution limits for security checks.</summary>
public sealed class SecurityCheckOptions
{
    /// <summary>Maximum runtime for one check. A hung provider cannot hang startup.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
