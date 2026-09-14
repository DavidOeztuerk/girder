using Girder.Redis.Caching;
using Girder.Abstractions.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;

namespace Girder.Redis.Caching;

/// <summary>
/// Redis-based distributed rate limiting store
/// </summary>
public class RedisDistributedRateLimitStore : IDistributedRateLimitStore
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisDistributedRateLimitStore> _logger;
    private readonly TimeProvider _time;

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
            -- The score determines age; the member identifies this request.
            -- Using the millisecond timestamp for both collapses a concurrent
            -- burst into one entry because sorted-set members are unique.
            redis.call('ZADD', key, now, ARGV[3] .. ':' .. ARGV[4])
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

    /// <param name="connectionMultiplexer">Shared connection to the RESP server.</param>
    /// <param name="logger">Receives storage failures.</param>
    public RedisDistributedRateLimitStore(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisDistributedRateLimitStore> logger)
        : this(connectionMultiplexer, logger, TimeProvider.System)
    {
    }

    /// <param name="connectionMultiplexer">Shared connection to the RESP server.</param>
    /// <param name="logger">Receives storage failures.</param>
    /// <param name="timeProvider">Clock used for sliding windows.</param>
    public RedisDistributedRateLimitStore(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisDistributedRateLimitStore> logger,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _time = timeProvider;
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

    public async Task<WindowCheckResult> SlidingWindowIncrementAsync(
        string key, 
        int limit, 
        TimeSpan window, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var windowMs = (long)window.TotalMilliseconds;
            var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
            var requestId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            var result = await _database.ScriptEvaluateAsync(
                SlidingWindowScript,
                new RedisKey[] { key },
                new RedisValue[] { windowMs, limit, now, requestId }
            );

            var resultArray = (RedisValue[])result!;
            var isAllowed = (long)resultArray[0] == 1;
            var currentCount = (long)resultArray[1];
            var limitValue = (long)resultArray[2];

            var resetTime = window;

            return new WindowCheckResult
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
            throw;
        }
    }

    /// <summary>
    /// Fixed window rate limiting implementation
    /// </summary>
    public async Task<WindowCheckResult> FixedWindowIncrementAsync(
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
                throw new InvalidOperationException(
                    $"Redis returned an invalid fixed-window result for key '{key}'.");
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

            return new WindowCheckResult
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
            throw;
        }
    }

    /// <summary>
    /// Get multiple rate limit results atomically
    /// </summary>
    public async Task<Dictionary<string, WindowCheckResult>> GetMultipleRateLimitsAsync(
        Dictionary<string, (int limit, TimeSpan window)> keyLimits,
        bool useSlidingWindow = true,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, WindowCheckResult>();

        foreach (var kvp in keyLimits)
        {
            var key = kvp.Key;
            var (limit, window) = kvp.Value;

            WindowCheckResult result;
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
