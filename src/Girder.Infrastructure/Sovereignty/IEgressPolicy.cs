namespace Girder.Infrastructure.Sovereignty;

/// <summary>
/// Decides which hosts the application is allowed to call.
/// </summary>
/// <remarks>
/// Data leaves a system through outbound calls, and usually through one nobody
/// meant to make: a telemetry exporter with a default endpoint, an SDK phoning
/// home, a dependency added for one feature. A declared policy turns that from
/// something you audit afterwards into something that fails at the call.
/// </remarks>
public interface IEgressPolicy
{
    /// <summary>Whether the application may call <paramref name="destination"/>.</summary>
    bool IsAllowed(Uri destination);

    /// <summary>The declared hosts, for reporting.</summary>
    IReadOnlyCollection<string> DeclaredHosts { get; }

    /// <summary>
    /// Whether the policy is enforcing. A policy with nothing declared allows
    /// everything, so that adding the package changes no behaviour until a
    /// declaration exists.
    /// </summary>
    bool IsEnforcing { get; }
}

/// <summary>
/// Thrown when an outbound call targets a host the egress policy does not allow.
/// </summary>
public sealed class EgressDeniedException : Exception
{
    public EgressDeniedException(Uri destination, IReadOnlyCollection<string> declaredHosts)
        : base($"Outbound call to '{destination.Host}' is not allowed. "
               + $"Declared hosts: {(declaredHosts.Count == 0 ? "none" : string.Join(", ", declaredHosts))}.")
    {
        Destination = destination;
        DeclaredHosts = declaredHosts;
    }

    public Uri Destination { get; }

    public IReadOnlyCollection<string> DeclaredHosts { get; }
}
