using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Caching;

/// <summary>
/// In-memory fallback implementation for rate limiting
/// </summary>
public class InMemoryRateLimitStore : IDistributedRateLimitStore
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryRateLimitStore> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

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

    public Task<long> ExecuteScriptAsync(string script, string[] keys, object[] values, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Script execution not supported in InMemoryRateLimitStore");
        return Task.FromResult(0L);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _cache.Remove(key);
        
        // Clean up semaphore
        if (_semaphores.TryRemove(key, out var semaphore))
        {
            semaphore.Dispose();
        }
        
        return Task.FromResult(true);
    }

    public async Task<RateLimitResult> SlidingWindowIncrementAsync(
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
            
            return new RateLimitResult
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
}