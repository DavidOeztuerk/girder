using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using System.IO.Compression;
using System.Text;

namespace Infrastructure.Caching;

/// <summary>
/// Redis-based distributed cache service with advanced features
/// </summary>
public class RedisDistributedCacheService : IDistributedCacheService
{
    private readonly IDatabase _database;
    private readonly IServer _server;
    private readonly ILogger<RedisDistributedCacheService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _keyPrefix;
    private readonly string _tagPrefix;

    // Lua script for tagged cache invalidation
    private const string InvalidateByTagScript = @"
        local tagKey = KEYS[1]
        local members = redis.call('SMEMBERS', tagKey)
        local count = 0
        for i = 1, #members do
            if redis.call('DEL', members[i]) > 0 then
                count = count + 1
            end
        end
        redis.call('DEL', tagKey)
        return count
    ";

    // Lua script for pattern-based deletion
    private const string DeleteByPatternScript = @"
        local pattern = ARGV[1]
        local keys = redis.call('KEYS', pattern)
        local count = 0
        for i = 1, #keys do
            if redis.call('DEL', keys[i]) > 0 then
                count = count + 1
            end
        end
        return count
    ";

    public RedisDistributedCacheService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisDistributedCacheService> logger,
        string keyPrefix = "cache:",
        string tagPrefix = "tag:")
    {
        _database = connectionMultiplexer.GetDatabase();
        _server = connectionMultiplexer.GetServer(connectionMultiplexer.GetEndPoints().First());
        _logger = logger;
        _keyPrefix = keyPrefix;
        _tagPrefix = tagPrefix;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var cacheKey = GetCacheKey(key);
            var value = await _database.StringGetAsync(cacheKey);

            if (!value.HasValue)
            {
                return null;
            }

            var cacheEntry = JsonSerializer.Deserialize<CacheEntry>(value!, _jsonOptions);
            
            if (cacheEntry == null)
            {
                _logger.LogWarning("Failed to deserialize cache entry for key {Key}", key);
                return null;
            }

            var data = cacheEntry.Compressed 
                ? DecompressData(cacheEntry.Data)
                : cacheEntry.Data;

            return JsonSerializer.Deserialize<T>(data, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache value for key {Key}", key);
            return null;
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key, 
        Func<Task<T>> factory, 
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var cached = await GetAsync<T>(key, cancellationToken);
        
        if (cached != null)
        {
            return cached;
        }

        var value = await factory();
        await SetAsync(key, value, expiration, cancellationToken: cancellationToken);
        
        return value;
    }

    public async Task SetAsync<T>(
        string key, 
        T value, 
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var cacheKey = GetCacheKey(key);
            var serializedValue = JsonSerializer.Serialize(value, _jsonOptions);
            
            var cacheEntry = new CacheEntry
            {
                Data = options?.Compress == true ? CompressData(serializedValue) : serializedValue,
                Compressed = options?.Compress == true,
                CreatedAt = DateTime.UtcNow,
                Tags = options?.Tags?.ToArray() ?? Array.Empty<string>()
            };

            var entryJson = JsonSerializer.Serialize(cacheEntry, _jsonOptions);
            
            // Set the cache entry
            if (expiration.HasValue)
            {
                await _database.StringSetAsync(cacheKey, entryJson, expiration);
            }
            else
            {
                await _database.StringSetAsync(cacheKey, entryJson);
            }

            // Handle tags for invalidation
            if (options?.Tags?.Any() == true)
            {
                await SetTagAssociationsAsync(cacheKey, options.Tags, expiration);
            }

            _logger.LogDebug("Cached value for key {Key} with expiration {Expiration}", key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set cache value for key {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GetCacheKey(key);
            await _database.KeyDeleteAsync(cacheKey);
            
            _logger.LogDebug("Removed cache entry for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache value for key {Key}", key);
        }
    }

    public async Task RemoveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKeys = keys.Select(GetCacheKey).ToArray();
            var redisKeys = cacheKeys.Select(k => (RedisKey)k).ToArray();
            
            await _database.KeyDeleteAsync(redisKeys);
            
            _logger.LogDebug("Removed {Count} cache entries", cacheKeys.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove multiple cache values");
        }
    }

    public async Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        try
        {
            var searchPattern = GetCacheKey(pattern);

            // Use SCAN instead of KEYS for better production performance (non-blocking)
            // SCAN is iterative and doesn't block Redis like KEYS does
            var deletedCount = 0;
            var cursor = 0L;
            var pageSize = 250; // Process in batches

            do
            {
                // SCAN returns cursor and matching keys
                var result = await _database.ExecuteAsync("SCAN", cursor.ToString(), "MATCH", searchPattern, "COUNT", pageSize.ToString());
                var scanResult = (RedisResult[])result!;

                cursor = long.Parse((string)scanResult[0]!);
                var keys = (RedisKey[])scanResult[1]!;

                if (keys.Length > 0)
                {
                    // Delete keys in batch
                    var deleted = await _database.KeyDeleteAsync(keys);
                    deletedCount += (int)deleted;
                }
            } while (cursor != 0 && !cancellationToken.IsCancellationRequested);

            _logger.LogDebug("Removed {Count} cache entries matching pattern {Pattern}", deletedCount, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache values by pattern {Pattern}", pattern);
        }
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        try
        {
            var tagKey = GetTagKey(tag);
            var deletedCount = await _database.ScriptEvaluateAsync(
                InvalidateByTagScript,
                keys: new RedisKey[] { tagKey });

            _logger.LogDebug("Removed {Count} cache entries with tag {Tag}", deletedCount, tag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove cache values by tag {Tag}", tag);
        }
    }

    public async Task RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken cancellationToken = default)
    {
        var tasks = tags.Select(tag => RemoveByTagAsync(tag, cancellationToken));
        await Task.WhenAll(tasks);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GetCacheKey(key);
            return await _database.KeyExistsAsync(cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of cache key {Key}", key);
            return false;
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await _server.InfoAsync("memory");
            var keyCount = await _server.DatabaseSizeAsync();
            
            var memoryUsage = 0L;
            var memoryGroup = info.FirstOrDefault(g => g.Key == "memory");
            var memoryValue = memoryGroup?.FirstOrDefault(kv => kv.Key == "used_memory").Value;
            if (!string.IsNullOrEmpty(memoryValue))
            {
                long.TryParse(memoryValue, out memoryUsage);
            }

            return new CacheStatistics
            {
                KeyCount = keyCount,
                MemoryUsage = memoryUsage,
                // InstanceInfo = $"Redis {await _server.InfoAsync("server").ContinueWith(t => t.Result.FirstOrDefault(x => x.Key == "redis_version").Value)}",
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cache statistics");
            return new CacheStatistics();
        }
    }

    public async Task RefreshAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = GetCacheKey(key);
            var ttl = await _database.KeyTimeToLiveAsync(cacheKey);
            
            if (ttl.HasValue)
            {
                await _database.KeyExpireAsync(cacheKey, ttl.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache key {Key}", key);
        }
    }

    public async Task<Dictionary<string, T?>> GetManyAsync<T>(IEnumerable<string> keys, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var cacheKeys = keys.Select(GetCacheKey).ToArray();
            var redisKeys = cacheKeys.Select(k => (RedisKey)k).ToArray();
            
            var values = await _database.StringGetAsync(redisKeys);
            var result = new Dictionary<string, T?>();

            for (int i = 0; i < keys.Count(); i++)
            {
                var key = keys.ElementAt(i);
                var value = values[i];

                if (value.HasValue)
                {
                    try
                    {
                        var cacheEntry = JsonSerializer.Deserialize<CacheEntry>(value!, _jsonOptions);
                        if (cacheEntry != null)
                        {
                            var data = cacheEntry.Compressed 
                                ? DecompressData(cacheEntry.Data)
                                : cacheEntry.Data;
                            result[key] = JsonSerializer.Deserialize<T>(data, _jsonOptions);
                        }
                        else
                        {
                            result[key] = null;
                        }
                    }
                    catch
                    {
                        result[key] = null;
                    }
                }
                else
                {
                    result[key] = null;
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get multiple cache values");
            return keys.ToDictionary(k => k, k => (T?)null);
        }
    }

    public async Task SetManyAsync<T>(
        Dictionary<string, T> keyValues, 
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var keyValuePairs = new List<KeyValuePair<RedisKey, RedisValue>>();

            foreach (var kvp in keyValues)
            {
                var cacheKey = GetCacheKey(kvp.Key);
                var serializedValue = JsonSerializer.Serialize(kvp.Value, _jsonOptions);
                
                var cacheEntry = new CacheEntry
                {
                    Data = options?.Compress == true ? CompressData(serializedValue) : serializedValue,
                    Compressed = options?.Compress == true,
                    CreatedAt = DateTime.UtcNow,
                    Tags = options?.Tags?.ToArray() ?? Array.Empty<string>()
                };

                var entryJson = JsonSerializer.Serialize(cacheEntry, _jsonOptions);
                keyValuePairs.Add(new KeyValuePair<RedisKey, RedisValue>(cacheKey, entryJson));
            }

            await _database.StringSetAsync(keyValuePairs.ToArray());

            // Set expiration if specified
            if (expiration.HasValue)
            {
                var tasks = keyValuePairs.Select(kvp => _database.KeyExpireAsync(kvp.Key, expiration.Value));
                await Task.WhenAll(tasks);
            }

            _logger.LogDebug("Cached {Count} values", keyValues.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set multiple cache values");
        }
    }

    public async Task WarmCacheAsync<T>(
        Dictionary<string, Func<Task<T>>> keyFactories,
        TimeSpan? expiration = null,
        CacheOptions? options = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var tasks = keyFactories.Select(async kvp =>
        {
            try
            {
                var value = await kvp.Value();
                await SetAsync(kvp.Key, value, expiration, options, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to warm cache for key {Key}", kvp.Key);
            }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Cache warming completed for {Count} keys", keyFactories.Count);
    }

    private string GetCacheKey(string key) => $"{_keyPrefix}{key}";
    private string GetTagKey(string tag) => $"{_tagPrefix}{tag}";

    private async Task SetTagAssociationsAsync(string cacheKey, HashSet<string> tags, TimeSpan? expiration)
    {
        var tasks = tags.Select(async tag =>
        {
            var tagKey = GetTagKey(tag);
            await _database.SetAddAsync(tagKey, cacheKey);
            
            if (expiration.HasValue)
            {
                await _database.KeyExpireAsync(tagKey, expiration.Value);
            }
        });

        await Task.WhenAll(tasks);
    }

    private static string CompressData(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        using var memoryStream = new MemoryStream();
        using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
        {
            gzipStream.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(memoryStream.ToArray());
    }

    private static string DecompressData(string compressedData)
    {
        var bytes = Convert.FromBase64String(compressedData);
        using var memoryStream = new MemoryStream(bytes);
        using var gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzipStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private class CacheEntry
    {
        public string Data { get; set; } = string.Empty;
        public bool Compressed { get; set; }
        public DateTime CreatedAt { get; set; }
        public string[] Tags { get; set; } = Array.Empty<string>();
    }
}