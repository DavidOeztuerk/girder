using Noelia.InMemory.Caching;
using Noelia.Abstractions.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Noelia.InMemory.Caching;

/// <summary>
/// In-memory fallback implementation for rate limiting
/// </summary>
public class InMemoryRateLimitStore : IDistributedRateLimitStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryRateLimitStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();
    private readonly ConcurrentDictionary<string, RateLimitCounterEntry> _observed = new();
    private readonly byte[] _fingerprintKey = RandomNumberGenerator.GetBytes(32);

    public InMemoryRateLimitStore(IMemoryCache cache, ILogger<InMemoryRateLimitStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<long> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        var count = _cache.TryGetValue(key, out var value) ? (long)(int)value! : 0L;
        return Task.FromResult(count);
    }

    public async Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var currentCount = _cache.TryGetValue(key, out var value) ? (int)value! : 0;
            var newCount = currentCount + 1;
            
            _cache.Set(key, newCount, expiration);
            Observe(key, newCount, null, false);
            return newCount;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var exists = _cache.TryGetValue(key, out _);
        return Task.FromResult(exists);
    }

    public Task<bool> ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(key, out var value))
        {
            _cache.Set(key, value, expiration);
            return Task.FromResult(true);
        }
        
        return Task.FromResult(false);
    }

    public Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default)
    {
        // IMemoryCache doesn't provide TTL information
        // Return null to indicate TTL is not available
        return Task.FromResult<TimeSpan?>(null);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        _observed.TryRemove(Fingerprint(key), out _);
        
        // Clean up semaphore
        if (_semaphores.TryRemove(key, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        return Task.FromResult(true);
    }

    public async Task<WindowCheckResult> SlidingWindowIncrementAsync(
        string key, 
        int limit, 
        TimeSpan window, 
        CancellationToken cancellationToken = default)
    {
        // Simple fixed-window implementation for in-memory fallback
        var semaphore = _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var slidingKey = $"{key}:sliding";
            var entries = _cache.Get<List<DateTimeOffset>>(slidingKey) ?? new List<DateTimeOffset>();
            
            var now = DateTimeOffset.UtcNow;
            var windowStart = now - window;
            
            // Remove expired entries
            entries = entries.Where(e => e > windowStart).ToList();
            
            var currentCount = entries.Count;
            var isAllowed = currentCount < limit;
            
            if (isAllowed)
            {
                entries.Add(now);
                _cache.Set(slidingKey, entries, window);
            }

            Observe(key, currentCount + (isAllowed ? 1 : 0), limit, !isAllowed);
            
            return new WindowCheckResult
            {
                IsAllowed = isAllowed,
                CurrentCount = currentCount + (isAllowed ? 1 : 0),
                Limit = limit,
                ResetTime = window
            };
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public Task<RateLimitInspection> InspectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RateLimitInspection(
            true,
            true,
            _observed.Values
                .OrderByDescending(entry => entry.ObservedAt)
                .Take(100)
                .ToArray()));
    }

    private void Observe(string key, long count, int? limit, bool rejected)
    {
        var fingerprint = Fingerprint(key);
        _observed[fingerprint] = new RateLimitCounterEntry(
            fingerprint, count, limit, rejected, DateTimeOffset.UtcNow);
    }

    private string Fingerprint(string key) =>
        Convert.ToHexString(HMACSHA256.HashData(_fingerprintKey, Encoding.UTF8.GetBytes(key)))[..12]
            .ToLowerInvariant();
}
