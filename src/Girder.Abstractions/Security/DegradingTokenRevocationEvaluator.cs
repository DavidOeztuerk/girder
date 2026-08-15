using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Girder.Abstractions.Security;

/// <summary>
/// Wraps an <see cref="ITokenRevocationEvaluator"/> so a brief store outage
/// neither signs everyone out nor lifts every revocation.
/// </summary>
/// <remarks>
/// Keeps the last verdict per token. While the inner evaluator answers, the
/// cache is only a record; once it stops answering, that record is served for
/// <see cref="RevocationDegradationOptions.MaxStaleness"/> and marked
/// <see cref="RevocationVerdict.IsStale"/>. After that the configured
/// <see cref="RevocationDegradationOptions.OnUnknown"/> decides.
/// <para>
/// Serving a stale verdict is a deliberate weakening, which is why it is
/// bounded, reported in the verdict, and logged. A revocation issued during the
/// outage is not visible until the store returns.
/// </para>
/// </remarks>
public sealed class DegradingTokenRevocationEvaluator : ITokenRevocationEvaluator
{
    private readonly ITokenRevocationEvaluator _inner;
    private readonly RevocationDegradationOptions _options;
    private readonly ILogger<DegradingTokenRevocationEvaluator> _logger;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<string, (RevocationVerdict Verdict, DateTimeOffset SeenAt)> _lastKnown =
        new(StringComparer.Ordinal);

    public DegradingTokenRevocationEvaluator(
        ITokenRevocationEvaluator inner,
        RevocationDegradationOptions options,
        ILogger<DegradingTokenRevocationEvaluator> logger,
        TimeProvider? timeProvider = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<RevocationVerdict> EvaluateAsync(
        TokenIdentity token,
        CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        try
        {
            using var budget = new CancellationTokenSource(_options.Budget);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                budget.Token, cancellationToken);

            var verdict = await _inner.EvaluateAsync(token, linked.Token);
            _lastKnown[token.TokenId] = (verdict, now);
            return verdict;
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                       || !cancellationToken.IsCancellationRequested)
        {
            return Degrade(token, now, ex);
        }
    }

    private RevocationVerdict Degrade(TokenIdentity token, DateTimeOffset now, Exception cause)
    {
        if (_lastKnown.TryGetValue(token.TokenId, out var known)
            && now - known.SeenAt <= _options.MaxStaleness)
        {
            _logger.LogWarning(
                cause,
                "Revocation store unreachable; serving a verdict {Age} old for token {TokenId}.",
                now - known.SeenAt, token.TokenId);

            return known.Verdict with { IsStale = true };
        }

        var denied = _options.OnUnknown == UnknownStatePolicy.Deny;

        _logger.LogError(
            cause,
            "Revocation store unreachable and no usable state for token {TokenId}; token {Outcome}.",
            token.TokenId, denied ? "refused" : "honoured");

        return new RevocationVerdict(denied, RevocationReason.StoreUnavailable, true);
    }
}
