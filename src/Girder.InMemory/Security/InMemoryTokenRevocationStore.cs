using System.Collections.Concurrent;
using Girder.Abstractions.Security;

namespace Girder.InMemory.Security;

/// <summary>
/// Keeps revocations in this process. Both the read and the write side, so a
/// single instance is consistent with itself.
/// </summary>
/// <remarks>
/// State lives in the process, so a second instance knows nothing of the first:
/// with more than one replica a revocation only reaches the instance that
/// recorded it. Use a shared provider there.
/// <para>
/// Entries are dropped lazily, when a key is next touched. Nothing scans in the
/// background, so memory is released on use rather than on time.
/// </para>
/// </remarks>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationEvaluator, ITokenRevocationWriter
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedSessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _cutoffs = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    public InMemoryTokenRevocationStore(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public ValueTask<RevocationVerdict> EvaluateAsync(
        TokenIdentity token,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        if (IsLive(_revokedTokens, TokenRevocationKeys.Token(token.TokenId), now))
        {
            return new(new RevocationVerdict(true, RevocationReason.TokenRevoked, false));
        }

        if (token.SessionId is { } sessionId
            && IsLive(_revokedSessions, TokenRevocationKeys.Session(token.SubjectId, sessionId), now))
        {
            return new(new RevocationVerdict(true, RevocationReason.SessionRevoked, false));
        }

        if (_cutoffs.TryGetValue(TokenRevocationKeys.SubjectCutoff(token.SubjectId), out var cutoff)
            && token.IssuedAt.ToUnixTimeSeconds() < cutoff)
        {
            return new(new RevocationVerdict(true, RevocationReason.SubjectCutoff, false));
        }

        return new(RevocationVerdict.Valid);
    }

    /// <inheritdoc />
    public Task RevokeTokenAsync(
        string tokenId,
        DateTimeOffset expiresAt,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        Keep(_revokedTokens, TokenRevocationKeys.Token(tokenId), expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeSessionAsync(
        string subjectId,
        string sessionId,
        DateTimeOffset expiresAt,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        Keep(_revokedSessions, TokenRevocationKeys.Session(subjectId, sessionId), expiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RevokeSubjectBeforeAsync(
        string subjectId,
        DateTimeOffset cutoff,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        var seconds = TokenRevocationKeys.ToCutoffSeconds(cutoff);

        // AddOrUpdate keeps the later of the two: concurrent revocations must
        // not move the cutoff backwards.
        _cutoffs.AddOrUpdate(
            TokenRevocationKeys.SubjectCutoff(subjectId),
            seconds,
            (_, existing) => Math.Max(existing, seconds));

        return Task.CompletedTask;
    }

    private static bool IsLive(
        ConcurrentDictionary<string, DateTimeOffset> entries,
        string key,
        DateTimeOffset now)
    {
        if (!entries.TryGetValue(key, out var expiresAt))
        {
            return false;
        }

        if (expiresAt > now)
        {
            return true;
        }

        entries.TryRemove(key, out _);
        return false;
    }

    private static void Keep(
        ConcurrentDictionary<string, DateTimeOffset> entries,
        string key,
        DateTimeOffset expiresAt) =>
        entries.AddOrUpdate(key, expiresAt, (_, existing) => existing > expiresAt ? existing : expiresAt);
}
