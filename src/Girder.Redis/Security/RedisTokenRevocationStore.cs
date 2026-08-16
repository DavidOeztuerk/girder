using Girder.Abstractions.Security;
using StackExchange.Redis;

namespace Girder.Redis.Security;

/// <summary>
/// Keeps revocations in Redis, so every instance sees the same decision.
/// Works against any RESP server — Redis, Valkey, Garnet, KeyDB.
/// </summary>
public sealed class RedisTokenRevocationStore : ITokenRevocationEvaluator, ITokenRevocationWriter
{
    /// <summary>
    /// Writes the cutoff only when it moves forward, and reports the value that
    /// ended up stored.
    /// </summary>
    /// <remarks>
    /// Read-compare-write from the client would let two concurrent revocations
    /// overwrite each other, and the loser could be the later one — that is the
    /// case where a revocation silently does nothing. The comparison therefore
    /// happens on the server, in one indivisible step.
    /// </remarks>
    private const string MonotonicCutoffScript = """
        local current = tonumber(redis.call('GET', KEYS[1]))
        local proposed = tonumber(ARGV[1])
        if current == nil or proposed > current then
            redis.call('SET', KEYS[1], proposed, 'EX', ARGV[2])
            return proposed
        end
        return current
        """;

    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly TimeSpan _maxTokenLifetime;

    /// <param name="connectionMultiplexer">Shared connection to the RESP server.</param>
    /// <param name="maxTokenLifetime">
    /// How long a cutoff is kept. Must be at least the longest lifetime an
    /// access token can have, or a cutoff expires while tokens it should refuse
    /// are still valid.
    /// </param>
    /// <param name="timeProvider">Clock used for entry retention; the system clock by default.</param>
    public RedisTokenRevocationStore(
        IConnectionMultiplexer connectionMultiplexer,
        TimeSpan maxTokenLifetime,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxTokenLifetime, TimeSpan.Zero);

        _database = connectionMultiplexer.GetDatabase();
        _maxTokenLifetime = maxTokenLifetime;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    /// <remarks>
    /// All three criteria travel in one batch: the check runs on every request,
    /// and three sequential round trips would triple that cost.
    /// </remarks>
    public async ValueTask<RevocationVerdict> EvaluateAsync(
        TokenIdentity token,
        CancellationToken cancellationToken = default)
    {
        var batch = _database.CreateBatch();

        var tokenRevoked = batch.KeyExistsAsync(TokenRevocationKeys.Token(token.TokenId));
        var sessionRevoked = token.SessionId is { } sessionId
            ? batch.KeyExistsAsync(TokenRevocationKeys.Session(token.SubjectId, sessionId))
            : Task.FromResult(false);
        var cutoff = batch.StringGetAsync(TokenRevocationKeys.SubjectCutoff(token.SubjectId));

        batch.Execute();
        await Task.WhenAll(tokenRevoked, sessionRevoked, cutoff).WaitAsync(cancellationToken);

        if (tokenRevoked.Result)
        {
            return new RevocationVerdict(true, RevocationReason.TokenRevoked, false);
        }

        if (sessionRevoked.Result)
        {
            return new RevocationVerdict(true, RevocationReason.SessionRevoked, false);
        }

        // Strictly less than, matching the round-up in ToCutoffSeconds: a token
        // stamped with the cutoff second itself is refused.
        if (cutoff.Result.TryParse(out long cutoffSeconds)
            && token.IssuedAt.ToUnixTimeSeconds() < cutoffSeconds)
        {
            return new RevocationVerdict(true, RevocationReason.SubjectCutoff, false);
        }

        return RevocationVerdict.Valid;
    }

    /// <inheritdoc />
    public async Task RevokeTokenAsync(
        string tokenId,
        DateTimeOffset expiresAt,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        await _database.StringSetAsync(
            TokenRevocationKeys.Token(tokenId),
            reason,
            RetentionFor(expiresAt));
    }

    /// <inheritdoc />
    public async Task RevokeSessionAsync(
        string subjectId,
        string sessionId,
        DateTimeOffset expiresAt,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        await _database.StringSetAsync(
            TokenRevocationKeys.Session(subjectId, sessionId),
            reason,
            RetentionFor(expiresAt));
    }

    /// <inheritdoc />
    public async Task RevokeSubjectBeforeAsync(
        string subjectId,
        DateTimeOffset cutoff,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        await _database.ScriptEvaluateAsync(
            MonotonicCutoffScript,
            [TokenRevocationKeys.SubjectCutoff(subjectId)],
            [TokenRevocationKeys.ToCutoffSeconds(cutoff), (long)_maxTokenLifetime.TotalSeconds]);
    }

    /// <summary>
    /// How long the entry is kept: until the token would have expired anyway,
    /// and at least one second so an entry written for an already-expired token
    /// is still observable.
    /// </summary>
    private TimeSpan RetentionFor(DateTimeOffset expiresAt)
    {
        var remaining = expiresAt - _time.GetUtcNow();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.FromSeconds(1);
    }
}
