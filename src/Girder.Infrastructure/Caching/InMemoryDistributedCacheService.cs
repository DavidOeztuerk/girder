using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Caching;

/// <summary>
/// In-memory implementation of IDistributedCacheService for development/fallback when Redis is unavailable
/// </summary>
public class InMemoryDistributedCacheService : IDistributedCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<InMemoryDistributedCacheService> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _tagToKeys = new();
    private readonly ConcurrentDictionary<string, byte> _keys = new();
    private readonly string _keyPrefix;

    // Statistics
    private long _hits;
    private long _misses;
    private long _evictions;

    public InMemoryDistributedCacheService(
        IMemoryCache cache,
        ILogger<InMemoryDistributedCacheService> logger,
        string keyPrefix = "")
    {
        _cache = cache;
        _logger = logger;
        _keyPrefix = keyPrefix;
        _logger.LogInformation("InMemoryDistributedCacheService initialized with prefix: {Prefix}", keyPrefix);
    }

    private string GetFullKey(string key) => string.IsNullOrEmpty(_keyPrefix) ? key : $"{_keyPrefix}{key}";

    public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = GetFullKey(key);
        if (_cache.TryGetValue(fullKey, out T? value))
        {
            Interlocked.Increment(ref _hits);
            return Task.FromResult(value);
        }

        Interlocked.Increment(ref _misses);
        return Task.FromResult<T?>(null);
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var existing = await GetAsync<T>(key, cancellationToken);
        if (existing != null)
            return existing;

        var value = await factory();
        await SetAsync(key, value, expiration, null, cancellationToken);
        return value;
    }

    public Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var fullKey = GetFullKey(key);
        var cacheOptions = new MemoryCacheEntryOptions();

        if (expiration.HasValue)
            cacheOptions.AbsoluteExpirationRelativeToNow = expiration;
        else
            cacheOptions.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30); // Default 30 min

        if (options?.SlidingExpiration.HasValue == true)
            cacheOptions.SlidingExpiration = options.SlidingExpiration;

        if (options?.AbsoluteExpiration.HasValue == true)
            cacheOptions.AbsoluteExpiration = options.AbsoluteExpiration;

        // Track evictions
        cacheOptions.RegisterPostEvictionCallback((evictedKey, evictedValue, reason, state) =>
        {
            if (reason != EvictionReason.Replaced)
            {
                Interlocked.Increment(ref _evictions);
                _keys.TryRemove(evictedKey.ToString()!, out _);
            }
        });

        _cache.Set(fullKey, value, cacheOptions);
        _keys.TryAdd(fullKey, 0);

        // Track tags
        if (options?.Tags != null)
        {
            foreach (var tag in options.Tags)
            {
                _tagToKeys.AddOrUpdate(
                    tag,
                    new HashSet<string> { fullKey },
                    (_, existing) =>
                    {
                        existing.Add(fullKey);
                        return existing;
                    });
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        _cache.Remove(fullKey);
        _keys.TryRemove(fullKey, out _);
        return Task.CompletedTask;
    }

    public async Task RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
        {
            await RemoveAsync(key, cancellationToken);
        }
    }

    public Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        // Simple pattern matching - convert wildcard to regex-like matching
        var keysToRemove = _keys.Keys
            .Where(k => MatchPattern(k, pattern))
            .ToList();

        foreach (var key in keysToRemove)
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
        }

        _logger.LogDebug("Removed {Count} keys matching pattern {Pattern}", keysToRemove.Count, pattern);
        return Task.CompletedTask;
    }

    private bool MatchPattern(string key, string pattern)
    {
        // Support simple wildcard patterns like "user:*" or "*:profile"
        if (pattern.EndsWith("*"))
        {
            var prefix = pattern[..^1];
            return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.StartsWith("*"))
        {
            var suffix = pattern[1..];
            return key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        return key.Equals(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (_tagToKeys.TryRemove(tag, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.Remove(key);
                _keys.TryRemove(key, out _);
            }
            _logger.LogDebug("Removed {Count} keys with tag {Tag}", keys.Count, tag);
        }
        return Task.CompletedTask;
    }

    public async Task RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        foreach (var tag in tags)
        {
            await RemoveByTagAsync(tag, cancellationToken);
        }
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = GetFullKey(key);
        return Task.FromResult(_cache.TryGetValue(fullKey, out _));
    }

    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CacheStatistics
        {
            Hits = _hits,
            Misses = _misses,
            KeyCount = _keys.Count,
            Evictions = _evictions,
            InstanceInfo = "InMemory",
            LastUpdated = DateTime.UtcNow
        });
    }

    public Task RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        // In-memory cache with sliding expiration auto-refreshes on access
        var fullKey = GetFullKey(key);
        _cache.TryGetValue(fullKey, out _);
        return Task.CompletedTask;
    }

    public Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        var result = new Dictionary<string, T?>();
        foreach (var key in keys)
        {
            var fullKey = GetFullKey(key);
            if (_cache.TryGetValue(fullKey, out T? value))
            {
                Interlocked.Increment(ref _hits);
                result[key] = value;
            }
            else
            {
                Interlocked.Increment(ref _misses);
                result[key] = null;
            }
        }
        return Task.FromResult(result);
    }

    public async Task SetManyAsync<T>(
        Dictionary<string, T> keyValues,
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        foreach (var (key, value) in keyValues)
        {
            await SetAsync(key, value, expiration, options, cancellationToken);
        }
    }

    public async Task WarmCacheAsync<T>(
        Dictionary<string, Func<Task<T>>> keyFactories,
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        foreach (var (key, factory) in keyFactories)
        {
            try
            {
                var value = await factory();
                await SetAsync(key, value, expiration, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm cache for key {Key}", key);
            }
        }
    }
}
