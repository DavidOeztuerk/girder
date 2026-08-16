using Microsoft.Extensions.DependencyInjection;

namespace Girder.Abstractions.Security;

/// <summary>What to answer when no state is known at all.</summary>
public enum UnknownStatePolicy
{
    /// <summary>Refuse the token. Safe, and the default.</summary>
    Deny,

    /// <summary>Honour the token. Keeps a service reachable during an outage at the cost of the check.</summary>
    Allow
}

/// <summary>
/// How the revocation check behaves while its store is unreachable.
/// </summary>
/// <remarks>
/// Two answers are available and both are wrong on their own: refusing every
/// request turns a brief store outage into a full sign-out, honouring every
/// request lifts every revocation for as long as the outage lasts. Bounded
/// staleness keeps the last known state for a stated span, then closes.
/// </remarks>
public sealed class RevocationDegradationOptions
{
    /// <summary>Configuration section this binds from.</summary>
    public const string SectionName = "TokenRevocation:Degradation";

    /// <summary>
    /// How long a cached decision may still be used after the store became
    /// unreachable. Past that, <see cref="OnUnknown"/> decides.
    /// </summary>
    public TimeSpan MaxStaleness { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// What to answer with no usable cached state — a cold start during an
    /// outage.
    /// </summary>
    public UnknownStatePolicy OnUnknown { get; set; } = UnknownStatePolicy.Deny;

    /// <summary>
    /// How long a single check may take before the store counts as unreachable.
    /// Runs on every request, so it is a latency budget, not a connection
    /// timeout.
    /// </summary>
    public TimeSpan Budget { get; set; } = TimeSpan.FromMilliseconds(50);
}

/// <summary>
/// Registration for token revocation. One of these must be called before the
/// middleware is added.
/// </summary>
public static class TokenRevocationRegistration
{
    /// <summary>
    /// Turns the revocation check off, on purpose.
    /// </summary>
    /// <param name="services">The container to register in.</param>
    /// <param name="rationale">
    /// Why this deployment does without it — written to the startup log. Defensible
    /// with very short access tokens whose revocation happens at the refresh path.
    /// </param>
    /// <remarks>
    /// Deliberately not the default: a revocation check that silently answers
    /// "not revoked" is indistinguishable from one that works, and the
    /// difference only shows when someone needs a token to stop working.
    /// </remarks>
    public static IServiceCollection AddNoTokenRevocation(
        this IServiceCollection services,
        string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);

        return services.AddSingleton<ITokenRevocationEvaluator>(
            new NoTokenRevocationEvaluator(rationale));
    }
}

/// <summary>
/// Honours every token. Registered only through
/// <see cref="TokenRevocationRegistration.AddNoTokenRevocation"/>, which
/// requires a stated reason.
/// </summary>
public sealed class NoTokenRevocationEvaluator : ITokenRevocationEvaluator
{
    public NoTokenRevocationEvaluator(string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Rationale = rationale;
    }

    /// <summary>Why the check is off. Meant for the startup log.</summary>
    public string Rationale { get; }

    /// <inheritdoc />
    public ValueTask<RevocationVerdict> EvaluateAsync(
        TokenIdentity token,
        CancellationToken cancellationToken = default) => new(RevocationVerdict.Valid);
}
