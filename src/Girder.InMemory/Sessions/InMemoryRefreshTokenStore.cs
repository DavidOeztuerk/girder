using Girder.Abstractions.Security.Sessions;
using Girder.Core.Identity;

namespace Girder.InMemory.Sessions;

/// <summary>
/// Keeps refresh tokens in this process.
/// </summary>
/// <remarks>
/// For tests and for a first run: nothing survives a restart, so everyone is
/// signed out by a deployment, and a second instance knows nothing of the
/// first. Put them in the database the application already runs before that
/// matters.
/// <para>
/// One lock around the whole transition. That is what the contract asks for and
/// it is trivially correct here — which is also why it proves nothing about a
/// database, and why the conformance suite is written to run against one.
/// </para>
/// </remarks>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<RefreshTokenId, RefreshTokenRecord> _tokens = [];
    private readonly Dictionary<string, RefreshTokenId> _byHash = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task CreateAsync(RefreshTokenRecord record, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _tokens[record.Id] = record;
            _byHash[Key(record.TokenHash)] = record.Id;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ConsumeResult> TryConsumeAsync(
        byte[] tokenHash,
        RefreshTokenRecord successor,
        DateTimeOffset now,
        TimeSpan grace,
        int maxConcurrentPerSession,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_byHash.TryGetValue(Key(tokenHash), out var id))
            {
                return Task.FromResult(new ConsumeResult(ConsumeOutcome.NotFound, null));
            }

            var current = _tokens[id];

            // Rotated already. Whether that is a race or a theft is decided by
            // how long ago — and inside the window the two are genuinely
            // indistinguishable, which is why the window is seconds.
            if (current.ReplacedBy is not null)
            {
                if (now <= current.RevokedAt!.Value + grace)
                {
                    var sibling = Rotate(current, successor, now, isGrace: true);
                    Cap(current.Session, maxConcurrentPerSession, now);
                    return Task.FromResult(
                        new ConsumeResult(ConsumeOutcome.RotatedWithinGrace, sibling));
                }

                CloseSession(current.Session, now);
                return Task.FromResult(new ConsumeResult(ConsumeOutcome.ReuseDetected, null));
            }

            if (current.RevokedAt is not null)
            {
                return Task.FromResult(new ConsumeResult(ConsumeOutcome.Revoked, null));
            }

            if (now >= current.ExpiresAt)
            {
                return Task.FromResult(new ConsumeResult(ConsumeOutcome.Expired, null));
            }

            if (now >= current.SessionExpiresAt)
            {
                return Task.FromResult(new ConsumeResult(ConsumeOutcome.SessionExpired, null));
            }

            var issued = Rotate(current, successor, now, isGrace: false);
            Cap(current.Session, maxConcurrentPerSession, now);

            return Task.FromResult(new ConsumeResult(ConsumeOutcome.Rotated, issued));
        }
    }

    /// <inheritdoc />
    public Task<int> CloseSessionAsync(
        SessionId session,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(CloseSession(session, at));
        }
    }

    /// <inheritdoc />
    public Task<int> CloseAllSessionsAsync(
        SubjectId subject,
        DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var sessions = _tokens.Values
                .Where(t => t.Subject == subject && IsOpen(t))
                .Select(t => t.Session)
                .Distinct()
                .ToArray();

            return Task.FromResult(sessions.Sum(session => CloseSession(session, at)));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SessionSummary>> ActiveSessionsAsync(
        SubjectId subject,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var summaries = _tokens.Values
                .Where(t => t.Subject == subject && IsOpen(t) && now < t.ExpiresAt && now < t.SessionExpiresAt)
                .GroupBy(t => t.Session)
                .Select(group => new SessionSummary(
                    group.Key,
                    group.Min(t => t.SessionStartedAt),
                    group.Max(t => t.IssuedAt),
                    group.Min(t => t.SessionExpiresAt),
                    group.OrderByDescending(t => t.IssuedAt).First().ClientFingerprint))
                .OrderByDescending(summary => summary.LastUsedAt)
                .ToArray();

            return Task.FromResult<IReadOnlyList<SessionSummary>>(summaries);
        }
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(
        DateTimeOffset olderThan,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

        lock (_gate)
        {
            var finished = _tokens.Values
                .Where(t => IsFinished(t, olderThan))
                .OrderBy(t => t.IssuedAt)
                .Take(batchSize)
                .ToArray();

            foreach (var token in finished)
            {
                _tokens.Remove(token.Id);
                _byHash.Remove(Key(token.TokenHash));
            }

            return Task.FromResult(finished.Length);
        }
    }

    /// <summary>How many tokens of a session are still usable. For tests.</summary>
    public int OpenTokenCount(SessionId session)
    {
        lock (_gate)
        {
            return _tokens.Values.Count(t => t.Session == session && IsOpen(t));
        }
    }

    /// <summary>
    /// Writes the successor and marks the predecessor as replaced.
    /// </summary>
    /// <remarks>
    /// The session's own attributes are copied rather than taken from the
    /// successor the caller built, so a caller cannot extend a sign-in past its
    /// ceiling by asking for a longer token.
    /// </remarks>
    private RefreshTokenRecord Rotate(
        RefreshTokenRecord current,
        RefreshTokenRecord successor,
        DateTimeOffset now,
        bool isGrace)
    {
        var issued = successor with
        {
            Session = current.Session,
            Subject = current.Subject,
            SessionStartedAt = current.SessionStartedAt,
            SessionExpiresAt = current.SessionExpiresAt,
            ExpiresAt = successor.ExpiresAt > current.SessionExpiresAt
                ? current.SessionExpiresAt
                : successor.ExpiresAt,
            IssuedAt = now,
            RevokedAt = null,
            ReplacedBy = null
        };

        _tokens[issued.Id] = issued;
        _byHash[Key(issued.TokenHash)] = issued.Id;

        // Inside the grace window the predecessor was already replaced; leaving
        // its first successor in place keeps the chain readable.
        if (!isGrace)
        {
            _tokens[current.Id] = current with { RevokedAt = now, ReplacedBy = issued.Id };
        }

        return issued;
    }

    /// <summary>Revokes every still-open token of a session.</summary>
    private int CloseSession(SessionId session, DateTimeOffset at)
    {
        var open = _tokens.Values.Where(t => t.Session == session && IsOpen(t)).ToArray();

        foreach (var token in open)
        {
            _tokens[token.Id] = token with { RevokedAt = at };
        }

        return open.Length;
    }

    /// <summary>
    /// Keeps a session's open tokens within its allowance, oldest first.
    /// </summary>
    private void Cap(SessionId session, int allowance, DateTimeOffset at)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(allowance, 1);

        var open = _tokens.Values
            .Where(t => t.Session == session && IsOpen(t))
            .OrderBy(t => t.IssuedAt)
            .ToArray();

        foreach (var token in open.Take(Math.Max(0, open.Length - allowance)))
        {
            _tokens[token.Id] = token with { RevokedAt = at };
        }
    }

    private static bool IsOpen(RefreshTokenRecord token) =>
        token.RevokedAt is null && token.ReplacedBy is null;

    private static bool IsFinished(RefreshTokenRecord token, DateTimeOffset olderThan) =>
        (token.RevokedAt is { } revoked && revoked < olderThan)
        || (token.ReplacedBy is not null && token.IssuedAt < olderThan)
        || token.ExpiresAt < olderThan;

    private static string Key(byte[] hash) => Convert.ToBase64String(hash);
}
