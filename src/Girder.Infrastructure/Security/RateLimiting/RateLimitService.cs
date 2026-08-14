using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Security.RateLimiting;

/// <summary>
/// Redis-based advanced rate limiting service
/// </summary>
public class RateLimitService : IRateLimitService
{
    private readonly IDatabase _database;
    private readonly ILogger<RateLimitService> _logger;
    private readonly string _keyPrefix;
    private readonly List<RateLimitRule> _rules;
    private readonly object _rulesLock = new();

    // Lua script for atomic rate limit checking with sliding window
    private const string SlidingWindowScript = @"
        local key = KEYS[1]
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local expiry = tonumber(ARGV[4])
        
        -- Remove expired entries
        redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
        
        -- Get current count
        local current = redis.call('ZCARD', key)
        
        if current < limit then
            -- Add current request
            redis.call('ZADD', key, now, now)
            redis.call('EXPIRE', key, expiry)
            return {1, current + 1, limit - current - 1}
        else
            return {0, current, 0}
        end
    ";

    // Lua script for token bucket algorithm
    private const string TokenBucketScript = @"
        local key = KEYS[1]
        local capacity = tonumber(ARGV[1])
        local refill_rate = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local expiry = tonumber(ARGV[4])
        
        -- Get current bucket state
        local bucket = redis.call('HMGET', key, 'tokens', 'last_refill')
        local tokens = tonumber(bucket[1]) or capacity
        local last_refill = tonumber(bucket[2]) or now
        
        -- Calculate tokens to add
        local time_passed = now - last_refill
        local new_tokens = math.min(capacity, tokens + (time_passed * refill_rate))
        
        if new_tokens >= 1 then
            -- Consume one token
            new_tokens = new_tokens - 1
            redis.call('HMSET', key, 'tokens', new_tokens, 'last_refill', now)
            redis.call('EXPIRE', key, expiry)
            return {1, math.floor(new_tokens), capacity}
        else
            -- Update without consuming
            redis.call('HMSET', key, 'tokens', new_tokens, 'last_refill', now)
            redis.call('EXPIRE', key, expiry)
            return {0, math.floor(new_tokens), capacity}
        end
    ";

    // Lua script for fixed window counter
    private const string FixedWindowScript = @"
        local key = KEYS[1]
        local limit = tonumber(ARGV[1])
        local window_start = tonumber(ARGV[2])
        local expiry = tonumber(ARGV[3])
        
        -- Create window-specific key
        local window_key = key .. ':' .. window_start
        
        -- Increment counter
        local current = redis.call('INCR', window_key)
        
        if current == 1 then
            redis.call('EXPIRE', window_key, expiry)
        end
        
        if current <= limit then
            return {1, current, limit - current}
        else
            return {0, current, 0}
        end
    ";

    public RateLimitService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RateLimitService> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _keyPrefix = "ratelimit:";
        _rules = new List<RateLimitRule>();
        
        InitializeDefaultRules();
    }

    public async Task<RateLimitResult> CheckRateLimitAsync(
        RateLimitRequest request, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check whitelist first
            if (await IsWhitelistedAsync(request.ClientId))
            {
                return CreateAllowedResult();
            }

            // Check blacklist
            if (await IsBlacklistedAsync(request.ClientId))
            {
                return CreateBlockedResult("Client is blacklisted", RateLimitSeverity.Critical);
            }

            // Find applicable rules
            var applicableRules = GetApplicableRules(request);
            
            if (!applicableRules.Any())
            {
                return CreateAllowedResult();
            }

            // Check each rule (highest priority first)
            foreach (var rule in applicableRules.OrderByDescending(r => r.Priority))
            {
                var result = await CheckRuleAsync(request, rule);
                
                if (!result.IsAllowed)
                {
                    // Log violation
                    await LogViolationAsync(request, rule, result);
                    
                    // Apply penalty if configured
                    await ApplyPenaltyAsync(request.ClientId, rule);
                    
                    return result;
                }
            }

            return CreateAllowedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking rate limit for client {ClientId}", request.ClientId);
            // Fail open - allow request if rate limiting fails
            return CreateAllowedResult();
        }
    }

    public Task RegisterRuleAsync(RateLimitRule rule, CancellationToken cancellationToken = default)
    {
        lock (_rulesLock)
        {
            // Remove existing rule with same ID
            _rules.RemoveAll(r => r.Id == rule.Id);
            
            // Add new rule
            _rules.Add(rule);
            
            _logger.LogInformation("Registered rate limit rule: {RuleId} - {RuleName}", rule.Id, rule.Name);
        }
        
        return Task.CompletedTask;
    }

    public Task RemoveRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        lock (_rulesLock)
        {
            var removed = _rules.RemoveAll(r => r.Id == ruleId);
            
            if (removed > 0)
            {
                _logger.LogInformation("Removed rate limit rule: {RuleId}", ruleId);
            }
        }
        
        return Task.CompletedTask;
    }

    public async Task<RateLimitStatus> GetStatusAsync(string clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            var status = new RateLimitStatus
            {
                ClientId = clientId,
                IsWhitelisted = await IsWhitelistedAsync(clientId),
                IsBlacklisted = await IsBlacklistedAsync(clientId)
            };

            // Get active windows for all rules
            foreach (var rule in _rules.Where(r => r.IsEnabled))
            {
                var window = await GetRateLimitWindowAsync(clientId, rule);
                if (window != null)
                {
                    status.ActiveWindows.Add(window);
                    
                    if (window.IsExceeded)
                    {
                        status.IsRateLimited = true;
                    }
                }
            }

            // Get recent violations
            status.RecentViolations = await GetRecentViolationsAsync(clientId);
            
            // Get penalty information
            var penaltyInfo = await GetPenaltyInfoAsync(clientId);
            status.PenaltyLevel = penaltyInfo.Level;
            status.PenaltyResetTime = penaltyInfo.ResetTime;

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rate limit status for client {ClientId}", clientId);
            return new RateLimitStatus { ClientId = clientId };
        }
    }

    public async Task ResetLimitsAsync(string clientId, CancellationToken cancellationToken = default)
    {
        try
        {
            var pattern = GetClientKeyPattern(clientId);
            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern);

            foreach (var key in keys)
            {
                await _database.KeyDeleteAsync(key);
            }

            _logger.LogInformation("Reset rate limits for client: {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting rate limits for client {ClientId}", clientId);
            throw;
        }
    }

    public async Task<RateLimitStatistics> GetStatisticsAsync(
        DateTime? fromDate = null, 
        DateTime? toDate = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = new RateLimitStatistics();
            
            // Get violation statistics from Redis
            var violationsKey = GetViolationsKey();
            var from = fromDate?.Ticks ?? 0;
            var to = toDate?.Ticks ?? DateTime.UtcNow.Ticks;
            
            var violations = await _database.SortedSetRangeByScoreAsync(violationsKey, from, to);
            
            foreach (var violation in violations)
            {
                try
                {
                    var violationData = JsonSerializer.Deserialize<RateLimitViolationData>(violation!);
                    if (violationData != null)
                    {
                        statistics.TotalViolations++;
                        
                        // Update top violating clients
                        if (statistics.TopViolatingClients.ContainsKey(violationData.ClientId))
                        {
                            statistics.TopViolatingClients[violationData.ClientId]++;
                        }
                        else
                        {
                            statistics.TopViolatingClients[violationData.ClientId] = 1;
                        }
                        
                        // Update top targeted endpoints
                        if (statistics.TopTargetedEndpoints.ContainsKey(violationData.Endpoint))
                        {
                            statistics.TopTargetedEndpoints[violationData.Endpoint]++;
                        }
                        else
                        {
                            statistics.TopTargetedEndpoints[violationData.Endpoint] = 1;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize violation data");
                }
            }

            // Sort top lists
            statistics.TopViolatingClients = statistics.TopViolatingClients
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            statistics.TopTargetedEndpoints = statistics.TopTargetedEndpoints
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rate limit statistics");
            return new RateLimitStatistics();
        }
    }

    public async Task SetClientLimitsAsync(
        string clientId, 
        Dictionary<string, RateLimitConfiguration> limits, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var clientLimitsKey = GetClientLimitsKey(clientId);
            var limitsJson = JsonSerializer.Serialize(limits);
            
            await _database.StringSetAsync(clientLimitsKey, limitsJson, TimeSpan.FromDays(30));
            
            _logger.LogInformation("Set custom limits for client: {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting client limits for {ClientId}", clientId);
            throw;
        }
    }

    public async Task WhitelistClientAsync(
        string clientId, 
        TimeSpan? duration = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var whitelistKey = GetWhitelistKey(clientId);
            var expiry = duration ?? TimeSpan.FromDays(365);
            
            await _database.StringSetAsync(whitelistKey, DateTime.UtcNow.ToString(), expiry);
            
            _logger.LogInformation("Whitelisted client: {ClientId} for {Duration}", clientId, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error whitelisting client {ClientId}", clientId);
            throw;
        }
    }

    public async Task BlacklistClientAsync(
        string clientId,
        TimeSpan? duration = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var blacklistKey = GetBlacklistKey(clientId);
            var expiry = duration ?? TimeSpan.FromHours(24);
            
            var blacklistData = new
            {
                Reason = reason ?? "Administrative action",
                BlacklistedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(expiry)
            };
            
            var dataJson = JsonSerializer.Serialize(blacklistData);
            await _database.StringSetAsync(blacklistKey, dataJson, expiry);
            
            _logger.LogWarning("Blacklisted client: {ClientId} for {Duration}, Reason: {Reason}", 
                clientId, expiry, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error blacklisting client {ClientId}", clientId);
            throw;
        }
    }

    public IEnumerable<RateLimitRule> GetRegisteredRules()
    {
        lock (_rulesLock)
        {
            return _rules.Select(r => r).ToList();
        }
    }

    #region Private Methods

    private async Task<RateLimitResult> CheckRuleAsync(RateLimitRequest request, RateLimitRule rule)
    {
        var key = GetRateLimitKey(request.ClientId, rule.Id);
        var config = rule.Configuration;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        long[] result = config.Algorithm switch
        {
            RateLimitAlgorithm.SlidingWindow => await ExecuteSlidingWindowAsync(key, config, now),
            RateLimitAlgorithm.TokenBucket => await ExecuteTokenBucketAsync(key, config, now),
            RateLimitAlgorithm.FixedWindow => await ExecuteFixedWindowAsync(key, config, now),
            _ => await ExecuteSlidingWindowAsync(key, config, now)
        };

        var isAllowed = result[0] == 1;
        var currentCount = result[1];
        var remaining = result[2];

        if (!isAllowed)
        {
            var retryAfter = CalculateRetryAfter(config, now);
            
            return new RateLimitResult
            {
                IsAllowed = false,
                TriggeredRule = rule,
                CurrentCount = currentCount,
                Limit = config.RequestLimit,
                Remaining = remaining,
                ResetTime = DateTime.UtcNow.Add(config.Window),
                RetryAfter = retryAfter,
                Reason = $"Rate limit exceeded for rule: {rule.Name}",
                Severity = CalculateSeverity(currentCount, config.RequestLimit),
                Headers = CreateRateLimitHeaders(currentCount, config.RequestLimit, remaining, retryAfter)
            };
        }

        return new RateLimitResult
        {
            IsAllowed = true,
            CurrentCount = currentCount,
            Limit = config.RequestLimit,
            Remaining = remaining,
            ResetTime = DateTime.UtcNow.Add(config.Window),
            Headers = CreateRateLimitHeaders(currentCount, config.RequestLimit, remaining, null)
        };
    }

    private async Task<long[]> ExecuteSlidingWindowAsync(string key, RateLimitConfiguration config, long now)
    {
        var windowSeconds = (long)config.Window.TotalSeconds;
        var expiry = (long)config.Window.TotalSeconds * 2; // Keep data longer for accuracy
        
        var result = await _database.ScriptEvaluateAsync(
            SlidingWindowScript,
            new RedisKey[] { key },
            new RedisValue[] { windowSeconds, config.RequestLimit, now, expiry }
        );

        return (long[])result!;
    }

    private async Task<long[]> ExecuteTokenBucketAsync(string key, RateLimitConfiguration config, long now)
    {
        var capacity = config.BurstLimit ?? config.RequestLimit;
        var refillRate = config.RefillRate ?? (double)config.RequestLimit / config.Window.TotalSeconds;
        var expiry = (long)config.Window.TotalSeconds * 2;
        
        var result = await _database.ScriptEvaluateAsync(
            TokenBucketScript,
            new RedisKey[] { key },
            new RedisValue[] { capacity, refillRate, now, expiry }
        );

        return (long[])result!;
    }

    private async Task<long[]> ExecuteFixedWindowAsync(string key, RateLimitConfiguration config, long now)
    {
        var windowStart = now - (now % (long)config.Window.TotalSeconds);
        var expiry = (long)config.Window.TotalSeconds;
        
        var result = await _database.ScriptEvaluateAsync(
            FixedWindowScript,
            new RedisKey[] { key },
            new RedisValue[] { config.RequestLimit, windowStart, expiry }
        );

        return (long[])result!;
    }

    private List<RateLimitRule> GetApplicableRules(RateLimitRequest request)
    {
        lock (_rulesLock)
        {
            return _rules.Where(rule => 
                rule.IsEnabled && 
                (rule.ExpiresAt == null || rule.ExpiresAt > DateTime.UtcNow) &&
                IsRuleApplicable(rule, request)
            ).ToList();
        }
    }

    private static bool IsRuleApplicable(RateLimitRule rule, RateLimitRequest request)
    {
        var conditions = rule.Conditions;

        // Check client IDs
        if (conditions.ClientIds.Any() && !conditions.ClientIds.Contains(request.ClientId))
            return false;

        // Check user roles
        if (conditions.UserRoles.Any() && !conditions.UserRoles.Any(role => request.UserRoles.Contains(role)))
            return false;

        // Check endpoints
        if (conditions.Endpoints.Any() && !conditions.Endpoints.Any(endpoint => MatchesPattern(request.Endpoint, endpoint)))
            return false;

        // Check HTTP methods
        if (conditions.Methods.Any() && !conditions.Methods.Contains(request.Method))
            return false;

        // Check IP patterns
        if (conditions.IpPatterns.Any() && request.IpAddress != null && 
            !conditions.IpPatterns.Any(pattern => MatchesPattern(request.IpAddress, pattern)))
            return false;

        // Check user agent patterns
        if (conditions.UserAgentPatterns.Any() && request.UserAgent != null && 
            !conditions.UserAgentPatterns.Any(pattern => MatchesPattern(request.UserAgent, pattern)))
            return false;

        // Check time conditions
        if (conditions.TimeConditions != null && !IsTimeConditionMet(conditions.TimeConditions))
            return false;

        // Check size conditions
        if (conditions.SizeConditions != null && !IsSizeConditionMet(conditions.SizeConditions, request))
            return false;

        // Check custom condition
        if (conditions.CustomCondition != null && !conditions.CustomCondition(request))
            return false;

        return true;
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase);
        }
        
        return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTimeConditionMet(TimeConditions timeConditions)
    {
        var now = DateTime.UtcNow;

        if (timeConditions.DaysOfWeek.Any() && !timeConditions.DaysOfWeek.Contains(now.DayOfWeek))
            return false;

        if (timeConditions.HoursOfDay.Any() && !timeConditions.HoursOfDay.Contains(now.Hour))
            return false;

        if (timeConditions.StartDate.HasValue && now < timeConditions.StartDate.Value)
            return false;

        if (timeConditions.EndDate.HasValue && now > timeConditions.EndDate.Value)
            return false;

        return true;
    }

    private static bool IsSizeConditionMet(SizeConditions sizeConditions, RateLimitRequest request)
    {
        if (!request.RequestSize.HasValue)
            return true;

        var size = request.RequestSize.Value;

        if (sizeConditions.MinSize.HasValue && size < sizeConditions.MinSize.Value)
            return false;

        if (sizeConditions.MaxSize.HasValue && size > sizeConditions.MaxSize.Value)
            return false;

        return true;
    }

    private async Task<bool> IsWhitelistedAsync(string clientId)
    {
        var whitelistKey = GetWhitelistKey(clientId);
        return await _database.KeyExistsAsync(whitelistKey);
    }

    private async Task<bool> IsBlacklistedAsync(string clientId)
    {
        var blacklistKey = GetBlacklistKey(clientId);
        return await _database.KeyExistsAsync(blacklistKey);
    }

    private async Task LogViolationAsync(RateLimitRequest request, RateLimitRule rule, RateLimitResult result)
    {
        try
        {
            var violationData = new RateLimitViolationData
            {
                ClientId = request.ClientId,
                UserId = request.UserId,
                Endpoint = request.Endpoint,
                Method = request.Method,
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                RuleId = rule.Id,
                RuleName = rule.Name,
                Timestamp = DateTime.UtcNow,
                CurrentCount = result.CurrentCount,
                Limit = result.Limit,
                Severity = result.Severity
            };

            var violationsKey = GetViolationsKey();
            var violationJson = JsonSerializer.Serialize(violationData);
            
            await _database.SortedSetAddAsync(violationsKey, violationJson, DateTime.UtcNow.Ticks);
            
            // Keep only last 30 days of violations
            var cutoff = DateTime.UtcNow.AddDays(-30).Ticks;
            await _database.SortedSetRemoveRangeByScoreAsync(violationsKey, 0, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging rate limit violation");
        }
    }

    private async Task ApplyPenaltyAsync(string clientId, RateLimitRule rule)
    {
        if (rule.Configuration.PenaltyFactor <= 1.0)
            return;

        try
        {
            var penaltyKey = GetPenaltyKey(clientId);
            var penaltyData = await _database.HashGetAllAsync(penaltyKey);
            
            var currentLevel = 1.0;
            var violations = 0;
            
            if (penaltyData.Any())
            {
                currentLevel = (double)penaltyData.FirstOrDefault(x => x.Name == "level").Value;
                violations = (int)penaltyData.FirstOrDefault(x => x.Name == "violations").Value;
            }

            var newLevel = Math.Min(currentLevel * rule.Configuration.PenaltyFactor, 10.0);
            var newViolations = violations + 1;
            var resetTime = DateTime.UtcNow.Add(rule.Configuration.MaxPenaltyDuration ?? TimeSpan.FromHours(1));

            await _database.HashSetAsync(penaltyKey, new HashEntry[]
            {
                new("level", newLevel),
                new("violations", newViolations),
                new("reset_time", resetTime.Ticks)
            });

            await _database.KeyExpireAsync(penaltyKey, rule.Configuration.MaxPenaltyDuration ?? TimeSpan.FromHours(1));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying penalty for client {ClientId}", clientId);
        }
    }

    private async Task<RateLimitWindow?> GetRateLimitWindowAsync(string clientId, RateLimitRule rule)
    {
        try
        {
            var key = GetRateLimitKey(clientId, rule.Id);
            var now = DateTime.UtcNow;
            
            if (rule.Configuration.Algorithm == RateLimitAlgorithm.SlidingWindow)
            {
                var windowStart = now.Subtract(rule.Configuration.Window);
                var count = await _database.SortedSetLengthAsync(key, windowStart.Ticks, now.Ticks);
                
                return new RateLimitWindow
                {
                    RuleId = rule.Id,
                    StartTime = windowStart,
                    EndTime = now,
                    RequestCount = count,
                    Limit = rule.Configuration.RequestLimit,
                    IsExceeded = count > rule.Configuration.RequestLimit
                };
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rate limit window");
            return null;
        }
    }

    private async Task<List<RateLimitViolation>> GetRecentViolationsAsync(string clientId)
    {
        try
        {
            var violationsKey = GetViolationsKey();
            var since = DateTime.UtcNow.AddHours(-24).Ticks;
            var violations = await _database.SortedSetRangeByScoreAsync(violationsKey, since, double.PositiveInfinity);
            
            var result = new List<RateLimitViolation>();
            
            foreach (var violation in violations)
            {
                try
                {
                    var violationData = JsonSerializer.Deserialize<RateLimitViolationData>(violation!);
                    if (violationData?.ClientId == clientId)
                    {
                        result.Add(new RateLimitViolation
                        {
                            Timestamp = violationData.Timestamp,
                            RuleId = violationData.RuleId,
                            Endpoint = violationData.Endpoint,
                            RequestCount = violationData.CurrentCount,
                            Limit = violationData.Limit,
                            Severity = violationData.Severity
                        });
                    }
                }
                catch (JsonException)
                {
                    // Skip invalid violation data
                }
            }
            
            return result.OrderByDescending(v => v.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent violations for client {ClientId}", clientId);
            return new List<RateLimitViolation>();
        }
    }

    private async Task<(double Level, DateTime? ResetTime)> GetPenaltyInfoAsync(string clientId)
    {
        try
        {
            var penaltyKey = GetPenaltyKey(clientId);
            var penaltyData = await _database.HashGetAllAsync(penaltyKey);
            
            if (!penaltyData.Any())
                return (1.0, null);

            var level = (double)penaltyData.FirstOrDefault(x => x.Name == "level").Value;
            var resetTimeTicks = (long)penaltyData.FirstOrDefault(x => x.Name == "reset_time").Value;
            var resetTime = resetTimeTicks > 0 ? new DateTime(resetTimeTicks) : (DateTime?)null;

            return (level, resetTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting penalty info for client {ClientId}", clientId);
            return (1.0, null);
        }
    }

    private void InitializeDefaultRules()
    {
        // Global rate limit
        RegisterRuleAsync(new RateLimitRule
        {
            Id = "global-default",
            Name = "Global Rate Limit",
            Description = "Default rate limit for all clients",
            Configuration = new RateLimitConfiguration
            {
                RequestLimit = 1000,
                Window = TimeSpan.FromMinutes(1),
                Algorithm = RateLimitAlgorithm.SlidingWindow
            },
            Conditions = new RateLimitConditions(),
            Priority = 1
        });

        // Authenticated users - higher limits
        RegisterRuleAsync(new RateLimitRule
        {
            Id = "authenticated-users",
            Name = "Authenticated Users",
            Description = "Higher limits for authenticated users",
            Configuration = new RateLimitConfiguration
            {
                RequestLimit = 5000,
                Window = TimeSpan.FromMinutes(1),
                Algorithm = RateLimitAlgorithm.SlidingWindow
            },
            Conditions = new RateLimitConditions
            {
                UserRoles = new List<string> { "User", "Premium", "Admin" }
            },
            Priority = 100
        });

        // Admin users - very high limits
        RegisterRuleAsync(new RateLimitRule
        {
            Id = "admin-users",
            Name = "Admin Users",
            Description = "Very high limits for admin users",
            Configuration = new RateLimitConfiguration
            {
                RequestLimit = 50000,
                Window = TimeSpan.FromMinutes(1),
                Algorithm = RateLimitAlgorithm.SlidingWindow
            },
            Conditions = new RateLimitConditions
            {
                UserRoles = new List<string> { "Admin", "SuperAdmin" }
            },
            Priority = 200
        });

        // Heavy endpoints - stricter limits
        RegisterRuleAsync(new RateLimitRule
        {
            Id = "heavy-endpoints",
            Name = "Heavy Endpoints",
            Description = "Stricter limits for resource-intensive endpoints",
            Configuration = new RateLimitConfiguration
            {
                RequestLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                Algorithm = RateLimitAlgorithm.TokenBucket,
                BurstLimit = 20,
                RefillRate = 0.167 // 10 per minute
            },
            Conditions = new RateLimitConditions
            {
                Endpoints = new List<string> { "/api/*/export", "/api/*/reports/*", "/api/*/search" }
            },
            Priority = 150
        });
    }

    private static RateLimitResult CreateAllowedResult()
    {
        return new RateLimitResult
        {
            IsAllowed = true,
            Remaining = long.MaxValue,
            Headers = new Dictionary<string, string>()
        };
    }

    private static RateLimitResult CreateBlockedResult(string reason, RateLimitSeverity severity)
    {
        return new RateLimitResult
        {
            IsAllowed = false,
            Reason = reason,
            Severity = severity,
            Headers = new Dictionary<string, string>()
        };
    }

    private static TimeSpan CalculateRetryAfter(RateLimitConfiguration config, long now)
    {
        return config.Algorithm switch
        {
            RateLimitAlgorithm.FixedWindow => TimeSpan.FromSeconds(config.Window.TotalSeconds - (now % (long)config.Window.TotalSeconds)),
            _ => config.Window
        };
    }

    private static RateLimitSeverity CalculateSeverity(long currentCount, long limit)
    {
        var ratio = (double)currentCount / limit;
        
        return ratio switch
        {
            > 5.0 => RateLimitSeverity.Critical,
            > 2.0 => RateLimitSeverity.Severe,
            > 1.5 => RateLimitSeverity.Warning,
            _ => RateLimitSeverity.Normal
        };
    }

    private static Dictionary<string, string> CreateRateLimitHeaders(long current, long limit, long remaining, TimeSpan? retryAfter)
    {
        var headers = new Dictionary<string, string>
        {
            ["X-RateLimit-Limit"] = limit.ToString(),
            ["X-RateLimit-Remaining"] = remaining.ToString(),
            ["X-RateLimit-Used"] = current.ToString()
        };

        if (retryAfter.HasValue)
        {
            headers["Retry-After"] = ((int)retryAfter.Value.TotalSeconds).ToString();
        }

        return headers;
    }

    // Key generation methods
    private string GetRateLimitKey(string clientId, string ruleId) => $"{_keyPrefix}limit:{clientId}:{ruleId}";
    private string GetWhitelistKey(string clientId) => $"{_keyPrefix}whitelist:{clientId}";
    private string GetBlacklistKey(string clientId) => $"{_keyPrefix}blacklist:{clientId}";
    private string GetPenaltyKey(string clientId) => $"{_keyPrefix}penalty:{clientId}";
    private string GetViolationsKey() => $"{_keyPrefix}violations";
    private string GetClientLimitsKey(string clientId) => $"{_keyPrefix}client_limits:{clientId}";
    private string GetClientKeyPattern(string clientId) => $"{_keyPrefix}*:{clientId}:*";

    #endregion
}

/// <summary>
/// Rate limit violation data for storage
/// </summary>
internal class RateLimitViolationData
{
    public string ClientId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public long CurrentCount { get; set; }
    public long Limit { get; set; }
    public RateLimitSeverity Severity { get; set; }
}