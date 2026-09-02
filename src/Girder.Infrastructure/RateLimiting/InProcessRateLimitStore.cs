using System.Collections.Concurrent;
using Girder.Abstractions.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Girder.Infrastructure.RateLimiting;

/// <summary>
/// Counts requests in this process, so rate limiting works before anyone has
/// chosen where counters live.
/// </summary>
/// <remarks>
/// <para>The same shape as the framework's own <c>AddDistributedMemoryCache()</c>,
/// which the default set already registers: in-process, needing no decision from
/// anyone, and replaced by the last registration when a provider package supplies
/// a real one. <c>AddRedisCache(prefix)</c> and <c>AddInMemoryCache(prefix)</c>
/// both register an <see cref="IDistributedRateLimitStore"/> and therefore win.</para>
///
/// <para><strong>It counts per process.</strong> Two replicas are two counters, so
/// a limit of 20 is 20 per replica. That is a reason to register a shared store
/// before running more than one instance — not a reason to leave the default set
/// unable to start, which is what naming no store at all produced: the pipeline
/// step refused to compose and the shortest documented way to stand a service up
/// died at startup.</para>
/// </remarks>
public sealed class InProcessRateLimitStore : IDistributedRateLimitStore
{
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Counts into the memory cache the default set already registers.</summary>
    /// <param name="cache">Where the counters live.</param>
    public InProcessRateLimitStore(IMemoryCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
    }

    /// <inheritdoc />
    public Task<long> GetCountAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cache.TryGetValue(key, out long count) ? count : 0L);

    /// <inheritdoc />
    public async Task<long> IncrementAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        // Read-modify-write, so two requests arriving together have to be kept
        // apart: without this the count under load is lower than the truth, which
        // is the one direction a rate limit must never be wrong in.
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = (_cache.TryGetValue(key, out long current) ? current : 0L) + 1L;

            // The window starts at the first request in it, so the expiry is set
            // from the value that is written, never extended by later ones.
            _cache.Set(key, next, expiration);
            return next;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cache.TryGetValue(key, out _));

    /// <inheritdoc />
    public Task<bool> ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (!_cache.TryGetValue(key, out long current))
        {
            return Task.FromResult(false);
        }

        _cache.Set(key, current, expiration);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always <c>null</c>: <see cref="IMemoryCache"/> keeps no expiry anyone can
    /// read back. Reporting a guess would be worse than reporting nothing, since
    /// the value ends up in a <c>Retry-After</c> a caller obeys.
    /// </remarks>
    public Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<TimeSpan?>(null);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        _cache.Remove(SlidingKey(key));

        if (_locks.TryRemove(key, out var gate))
        {
            gate.Dispose();
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Counting and deciding happen inside one lock, which is what the port
    /// demands: two callers arriving together at the limit must not both be told
    /// they are under it.
    /// </remarks>
    public async Task<WindowCheckResult> SlidingWindowIncrementAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var slidingKey = SlidingKey(key);
            var seen = _cache.Get<List<DateTimeOffset>>(slidingKey) ?? [];

            var now = DateTimeOffset.UtcNow;
            var windowStart = now - window;

            // Genuinely sliding: what fell out of the window is dropped on read,
            // so a burst at the end of one window does not carry into the next.
            seen = seen.Where(at => at > windowStart).ToList();

            var allowed = seen.Count < limit;
            if (allowed)
            {
                seen.Add(now);
                _cache.Set(slidingKey, seen, window);
            }

            return new WindowCheckResult
            {
                IsAllowed = allowed,
                CurrentCount = seen.Count,
                Limit = limit,
                ResetTime = window
            };
        }
        finally
        {
            gate.Release();
        }
    }

    private static string SlidingKey(string key) => $"{key}:sliding";
}
