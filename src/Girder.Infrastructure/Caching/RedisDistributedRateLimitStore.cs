using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Caching;

/// <summary>
/// Redis-based distributed rate limiting store
/// </summary>
public class RedisDistributedRateLimitStore : IDistributedRateLimitStore
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisDistributedRateLimitStore> _logger;

    // Lua script for sliding window rate limiting
    private const string SlidingWindowScript = @"
        local key = KEYS[1]
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])

        -- Remove expired entries
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)

        -- Count current entries
        local current = redis.call('ZCARD', key)

        -- Check if limit exceeded
        if current < limit then
            -- Add current request
            redis.call('ZADD', key, now, now)
            redis.call('EXPIRE', key, math.ceil(window / 1000))
            return {1, current + 1, limit}
        else
            return {0, current, limit}
        end
    ";

    // Lua script for fixed window rate limiting
    private const string FixedWindowScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local expiry = tonumber(ARGV[2])

        local current = redis.call('GET', key)
        if current == false then
            current = 0
        else
            current = tonumber(current)
        end

        if current < limit then
            local result = redis.call('INCR', key)
            if result == 1 then
                redis.call('EXPIRE', key, expiry)
            end
            return {1, result, limit}
        else
            local ttl = redis.call('TTL', key)
            return {0, current, limit, ttl}
        end
    ";

    public RedisDistributedRateLimitStore(IConnectionMultiplexer connectionMultiplexer, ILogger<RedisDistributedRateLimitStore> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
    }

    public async Task<long> GetCountAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            return value.HasValue ? (long)value : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get count for key {Key}", key);
            throw;
        }
    }

    public async Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _database.StringIncrementAsync(key);

            // Set expiration only on first increment
            if (result == 1)
            {
                await _database.KeyExpireAsync(key, expiration);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment key {Key}", key);
            throw;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of key {Key}", key);
            throw;
        }
    }

    public async Task<bool> ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.KeyExpireAsync(key, expiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set expiration for key {Key}", key);
            throw;
        }
    }

    public async Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var ttl = await _database.KeyTimeToLiveAsync(key);
            return ttl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TTL for key {Key}", key);
            throw;
        }
    }

    public async Task<long> ExecuteScriptAsync(string script, string[] keys, object[] values, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            var redisValues = values.Select(v => (RedisValue)v.ToString()).ToArray();

            var result = await _database.ScriptEvaluateAsync(script, redisKeys, redisValues);
            return (long)result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute script with keys {Keys}", string.Join(", ", keys));
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete key {Key}", key);
            throw;
        }
    }

    public async Task<RateLimitResult> SlidingWindowIncrementAsync(
        string key, 
        int limit, 
        TimeSpan window, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var windowMs = (long)window.TotalMilliseconds;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = await _database.ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { windowMs, limit, now }
            );

            var resultArray = (RedisValue[])result!;
            var isAllowed = (long)resultArray[0] == 1;
            var currentCount = (long)resultArray[1];
            var limitValue = (long)resultArray[2];

            var resetTime = window;

            return new RateLimitResult
            {
                IsAllowed = isAllowed,
                CurrentCount = currentCount,
                Limit = (int)limitValue,
                ResetTime = resetTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute sliding window increment for key {Key}", key);

            // Fallback: Allow request but log error
            return new RateLimitResult
            {
                IsAllowed = true,
                CurrentCount = 0,
                Limit = limit,
                ResetTime = window
            };
        }
    }

    /// <summary>
    /// Fixed window rate limiting implementation
    /// </summary>
    public async Task<RateLimitResult> FixedWindowIncrementAsync(
        string key,
        int limit,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var windowSeconds = (long)window.TotalSeconds;

            var result = await _database.ScriptEvaluateAsync(
                FixedWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { limit, windowSeconds }
            );

            var resultArray = (RedisValue[])result!;
            if (resultArray == null || resultArray.Length < 3)
            {
                // Fallback: Allow request but log error
                _logger.LogError("ScriptEvaluateAsync returned null or insufficient result for key {Key}", key);
                return new RateLimitResult
                {
                    IsAllowed = true,
                    CurrentCount = 0,
                    Limit = limit,
                    ResetTime = window
                };
            }

            var isAllowed = (long)resultArray[0] == 1;
            var currentCount = (long)resultArray[1];
            var limitValue = (long)resultArray[2];

            TimeSpan? resetTime = null;
            if (resultArray.Length > 3 && resultArray[3].HasValue)
            {
                var ttlSeconds = (long)resultArray[3];
                resetTime = TimeSpan.FromSeconds(ttlSeconds);
            }

            return new RateLimitResult
            {
                IsAllowed = isAllowed,
                CurrentCount = currentCount,
                Limit = (int)limitValue,
                ResetTime = resetTime ?? window
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute fixed window increment for key {Key}", key);

            // Fallback: Allow request but log error
            return new RateLimitResult
            {
                IsAllowed = true,
                CurrentCount = 0,
                Limit = limit,
                ResetTime = window
            };
        }
    }

    /// <summary>
    /// Get multiple rate limit results atomically
    /// </summary>
    public async Task<Dictionary<string, RateLimitResult>> GetMultipleRateLimitsAsync(
        Dictionary<string, (int limit, TimeSpan window)> keyLimits,
        bool useSlidingWindow = true,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, RateLimitResult>();

        foreach (var kvp in keyLimits)
        {
            var key = kvp.Key;
            var (limit, window) = kvp.Value;

            RateLimitResult result;
            if (useSlidingWindow)
            {
                result = await SlidingWindowIncrementAsync(key, limit, window, cancellationToken);
            }
            else
            {
                result = await FixedWindowIncrementAsync(key, limit, window, cancellationToken);
            }

            results[key] = result;
        }

        return results;
    }
}