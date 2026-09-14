using Noelia.Core.Identity;

namespace Noelia.Abstractions.Security.Sessions;

/// <summary>What a sign-in produced.</summary>
/// <param name="Session">Names the sign-in for its whole life.</param>
/// <param name="RefreshToken">The one issued value; stores retain only its hash.</param>
/// <param name="ExpiresAt">When this token stops working.</param>
public sealed record SignInResult(SessionId Session, string RefreshToken, DateTimeOffset ExpiresAt);

/// <summary>What presenting a refresh token produced.</summary>
public sealed record RefreshResult(
    ConsumeOutcome Outcome,
    SessionId? Session,
    SubjectId? Subject,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt)
{
    /// <summary>Whether the caller may issue a new access token.</summary>
    public bool Succeeded =>
        Outcome is ConsumeOutcome.Rotated or ConsumeOutcome.RotatedWithinGrace;
}

/// <summary>Signing in, refreshing and signing out.</summary>
public interface ITokenSessionService
{
    /// <summary>Starts a sign-in and issues its first refresh token.</summary>
    Task<SignInResult> SignInAsync(
        SubjectId subject,
        string? clientFingerprint = null,
        CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for its successor.</summary>
    Task<RefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>Ends one sign-in.</summary>
    Task SignOutAsync(SessionId session, CancellationToken cancellationToken = default);

    /// <summary>Ends every sign-in of one person.</summary>
    Task SignOutEverywhereAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default);

    /// <summary>The sign-ins a person currently holds.</summary>
    Task<IReadOnlyList<SessionSummary>> ActiveSessionsAsync(
        SubjectId subject,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a token-free view of sessions observed by this service instance.
    /// Implementations that cannot enumerate safely report unavailable.
    /// </summary>
    Task<TokenSessionInspection> InspectAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(TokenSessionInspection.Unavailable);
}

/// <summary>One active session without a token or raw device fingerprint.</summary>
public sealed record TokenSessionEntry(
    string Subject,
    string Session,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string ClientFingerprintShape);

/// <summary>The sessions this instance has observed, not a cluster-wide inventory.</summary>
public sealed record TokenSessionInspection(
    bool IsAvailable,
    bool IsInstanceScoped,
    IReadOnlyList<TokenSessionEntry> Sessions)
{
    /// <summary>An implementation with no safe operator read model.</summary>
    public static TokenSessionInspection Unavailable { get; } =
        new(false, true, Array.Empty<TokenSessionEntry>());
}
