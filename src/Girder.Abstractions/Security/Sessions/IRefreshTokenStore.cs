using Girder.Core.Identity;

namespace Girder.Abstractions.Security.Sessions;

/// <summary>
/// Where refresh tokens live. Implement it over the database the application
/// already runs — this needs no server of its own.
/// </summary>
/// <remarks>
/// Rotation is one method rather than find-then-update on purpose. Split in
/// two, a second request can consume the same row between the two calls, and an
/// interface that permits a race gets implementations that have one.
/// </remarks>
public interface IRefreshTokenStore
{
    /// <summary>Writes the first token of a new sign-in.</summary>
    Task CreateAsync(RefreshTokenRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes the token with <paramref name="tokenHash"/> and writes
    /// <paramref name="successor"/>, indivisibly.
    /// </summary>
    /// <remarks>
    /// The transition may succeed only if the row has not been consumed
    /// already. Concurrent callers must see
    /// <see cref="ConsumeOutcome.RotatedWithinGrace"/> or
    /// <see cref="ConsumeOutcome.ReuseDetected"/>, never a second
    /// <see cref="ConsumeOutcome.Rotated"/> — an implementation that lets two
    /// callers both rotate has handed the same session to two holders.
    /// <para>
    /// Within <paramref name="grace"/> of the original rotation the loser of a
    /// race is issued a fresh token for the same session rather than being
    /// treated as a thief. The successor is not re-delivered: only its hash was
    /// kept, and keeping the token itself would give up the reason for hashing.
    /// </para>
    /// </remarks>
    /// <param name="tokenHash">SHA-256 of the presented token.</param>
    /// <param name="successor">The record to write if the transition is allowed.</param>
    /// <param name="now">The moment to judge expiry and grace against.</param>
    /// <param name="grace">
    /// How long after a rotation a second presentation is still a race rather
    /// than a theft. Seconds, not minutes: inside this window a replay by an
    /// attacker is indistinguishable from a second tab, so it is a deliberate
    /// and bounded concession.
    /// </param>
    /// <param name="maxConcurrentPerSession">
    /// How many unconsumed tokens one session may hold. Each rotation inside
    /// the grace window adds a sibling, and a client retry loop would otherwise
    /// grow the session without limit. On overflow the oldest is revoked.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ConsumeResult> TryConsumeAsync(
        byte[] tokenHash,
        RefreshTokenRecord successor,
        DateTimeOffset now,
        TimeSpan grace,
        int maxConcurrentPerSession,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ends one sign-in. Idempotent.
    /// </summary>
    /// <remarks>
    /// Named "close" rather than "revoke" because it takes effect at the next
    /// refresh, not on the next request: an access token already issued stands
    /// until it expires. <c>ITokenRevocationWriter.RevokeSessionAsync</c> is the
    /// one that acts immediately, and needs a store on the reading side.
    /// </remarks>
    /// <returns>How many tokens were still open.</returns>
    Task<int> CloseSessionAsync(
        SessionId session,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>Ends every sign-in of one person. Idempotent.</summary>
    Task<int> CloseAllSessionsAsync(
        SubjectId subject,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The sign-ins a person currently holds, newest first.
    /// </summary>
    /// <remarks>
    /// So a person can see their own sessions and end one. Also what answers a
    /// request for access under GDPR Article 15 without keeping any record of
    /// behaviour.
    /// </remarks>
    Task<IReadOnlyList<SessionSummary>> ActiveSessionsAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes rows that expired or were closed before <paramref name="olderThan"/>.
    /// </summary>
    /// <remarks>
    /// In batches, and called by the application on its own schedule. Girder
    /// runs no background loop: retention is policy, the table belongs to the
    /// application, and a bulk delete competes with
    /// <see cref="TryConsumeAsync"/> for the same pages — on a single-writer
    /// database that blocks the sign-in path.
    /// </remarks>
    /// <param name="olderThan">Cut-off; rows still in use are never touched.</param>
    /// <param name="batchSize">Rows per statement.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>How many rows were removed.</returns>
    Task<int> PurgeAsync(
        DateTimeOffset olderThan,
        int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>One sign-in, as a person sees it in their own list.</summary>
/// <param name="Session">Names it, so it can be ended.</param>
/// <param name="StartedAt">When the sign-in happened.</param>
/// <param name="LastUsedAt">When it was last refreshed.</param>
/// <param name="ExpiresAt">When it ends on its own.</param>
/// <param name="ClientFingerprint">Whatever the application chose to record, if anything.</param>
public readonly record struct SessionSummary(
    SessionId Session,
    DateTimeOffset StartedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? ClientFingerprint);
