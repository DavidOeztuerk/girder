using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure.Security;

/// <summary>
/// Redis-based token revocation service for scalable token management
/// </summary>
public class RedisTokenRevocationService : ITokenRevocationService
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisTokenRevocationService> _logger;
    private readonly string _keyPrefix;
    private readonly string _userTokensPrefix;
    private readonly string _refreshTokenPrefix;

    // Lua script for atomic token revocation with metadata
    private const string RevokeTokenScript = @"
        local jtiKey = KEYS[1]
        local userTokensKey = KEYS[2]
        local tokenData = ARGV[1]
        local expiry = tonumber(ARGV[2])
        
        -- Set the revoked token data
        redis.call('SET', jtiKey, tokenData, 'EX', expiry)
        
        -- Add to user's revoked tokens set
        redis.call('SADD', userTokensKey, jtiKey)
        redis.call('EXPIRE', userTokensKey, expiry)
        
        return 1
    ";

    // Lua script for revoking all user tokens
    private const string RevokeUserTokensScript = @"
        local userTokensKey = KEYS[1]
        local userPattern = ARGV[1]
        local expiry = tonumber(ARGV[2])
        
        -- Get all user tokens
        local userTokens = redis.call('SMEMBERS', userTokensKey)
        local revokedCount = 0
        
        -- Mark pattern for user tokens as revoked
        redis.call('SET', userPattern, '1', 'EX', expiry)
        
        -- Extend expiry of existing revoked tokens
        for i = 1, #userTokens do
            redis.call('EXPIRE', userTokens[i], expiry)
            revokedCount = revokedCount + 1
        end
        
        redis.call('EXPIRE', userTokensKey, expiry)
        
        return revokedCount
    ";

    public RedisTokenRevocationService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisTokenRevocationService> logger,
        string keyPrefix = "revoked:")
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _keyPrefix = keyPrefix;
        _userTokensPrefix = $"{keyPrefix}user:";
        _refreshTokenPrefix = $"{keyPrefix}refresh:";
    }

    public async Task RevokeTokenAsync(string jti, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var request = new TokenRevocationRequest
        {
            Jti = jti,
            Reason = TokenRevocationReason.UserLogout,
            TokenExpiry = expiry
        };

        await RevokeTokenAsync(request, cancellationToken);
    }

    public async Task RevokeTokenAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var jtiKey = GetJtiKey(request.Jti);
            var userTokensKey = GetUserTokensKey(request.UserId);
            
            var revokedInfo = new RevokedTokenInfo
            {
                Jti = request.Jti,
                UserId = request.UserId,
                RevokedAt = DateTime.UtcNow,
                Reason = request.Reason,
                Details = request.Details,
                RevokedFromIp = request.RevokedFromIp,
                RevokedFromUserAgent = request.RevokedFromUserAgent,
                ExpiresAt = DateTime.UtcNow.Add(request.TokenExpiry ?? TimeSpan.FromDays(30))
            };

            var tokenData = JsonSerializer.Serialize(revokedInfo);
            var expirySeconds = (long)(request.TokenExpiry?.TotalSeconds ?? TimeSpan.FromDays(30).TotalSeconds);

            await _database.ScriptEvaluateAsync(
                RevokeTokenScript,
                new RedisKey[] { jtiKey, userTokensKey },
                new RedisValue[] { tokenData, expirySeconds }
            );

            _logger.LogInformation(
                "Token revoked: JTI={Jti}, UserId={UserId}, Reason={Reason}",
                request.Jti, request.UserId, request.Reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "Failed to revoke token: JTI={Jti}, UserId={UserId}", 
                request.Jti, request.UserId);
            throw;
        }
    }

    public async Task RevokeUserTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userTokensKey = GetUserTokensKey(userId);
            var userPatternKey = GetUserPatternKey(userId);
            var expirySeconds = (long)TimeSpan.FromDays(30).TotalSeconds;

            var revokedCount = await _database.ScriptEvaluateAsync(
                RevokeUserTokensScript,
                new RedisKey[] { userTokensKey },
                new RedisValue[] { userPatternKey, expirySeconds }
            );

            _logger.LogInformation(
                "All tokens revoked for user: UserId={UserId}, Count={Count}",
                userId, revokedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke user tokens: UserId={UserId}", userId);
            throw;
        }
    }

    public async Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        try
        {
            var jtiKey = GetJtiKey(jti);
            return await _database.KeyExistsAsync(jtiKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check token revocation status: JTI={Jti}", jti);
            // On error, assume token is not revoked to avoid blocking valid requests
            return false;
        }
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var refreshTokenKey = GetRefreshTokenKey(refreshToken);
            var expiry = TimeSpan.FromDays(30); // Refresh tokens typically have longer expiry
            
            await _database.StringSetAsync(refreshTokenKey, DateTime.UtcNow.ToString(), expiry);
            
            _logger.LogInformation("Refresh token revoked: {RefreshTokenHash}", HashToken(refreshToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke refresh token");
            throw;
        }
    }

    public async Task<bool> IsRefreshTokenRevokedAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var refreshTokenKey = GetRefreshTokenKey(refreshToken);
            return await _database.KeyExistsAsync(refreshTokenKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check refresh token revocation status");
            return false;
        }
    }

    public async Task<IEnumerable<RevokedTokenInfo>> GetRevokedTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var userTokensKey = GetUserTokensKey(userId);
            var revokedTokenKeys = await _database.SetMembersAsync(userTokensKey);
            
            var revokedTokens = new List<RevokedTokenInfo>();
            
            foreach (var tokenKey in revokedTokenKeys)
            {
                try
                {
                    // // var tokenData = await _database.StringGetAsync(tokenKey);
                    // if (tokenData.HasValue)
                    // {
                    //     var revokedInfo = JsonSerializer.Deserialize<RevokedTokenInfo>(tokenData!);
                    //     if (revokedInfo != null)
                    //     {
                    //         revokedTokens.Add(revokedInfo);
                    //     }
                    // }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize revoked token data: {TokenKey}", tokenKey);
                }
            }

            return revokedTokens.OrderByDescending(t => t.RevokedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get revoked tokens for user: UserId={UserId}", userId);
            return Enumerable.Empty<RevokedTokenInfo>();
        }
    }

    public async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
            var pattern = $"{_keyPrefix}*";
            var keys = server.Keys(pattern: pattern, pageSize: 1000);
            
            var deletedCount = 0;
            var batchSize = 100;
            var batch = new List<RedisKey>();

            foreach (var key in keys)
            {
                batch.Add(key);

                if (batch.Count >= batchSize)
                {
                    deletedCount += await ProcessCleanupBatch(batch);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                deletedCount += await ProcessCleanupBatch(batch);
            }

            _logger.LogInformation("Token cleanup completed: {DeletedCount} expired tokens removed", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired tokens");
        }
    }

    private async Task<int> ProcessCleanupBatch(List<RedisKey> keys)
    {
        var expiredKeys = new List<RedisKey>();
        
        foreach (var key in keys)
        {
            try
            {
                var tokenData = await _database.StringGetAsync(key);
                if (tokenData.HasValue)
                {
                    var revokedInfo = JsonSerializer.Deserialize<RevokedTokenInfo>(tokenData!);
                    if (revokedInfo?.ExpiresAt < DateTime.UtcNow)
                    {
                        expiredKeys.Add(key);
                    }
                }
                else
                {
                    // Key doesn't exist or is empty, mark for deletion
                    expiredKeys.Add(key);
                }
            }
            catch (JsonException)
            {
                // Corrupted data, mark for deletion
                expiredKeys.Add(key);
            }
        }

        if (expiredKeys.Count > 0)
        {
            await _database.KeyDeleteAsync(expiredKeys.ToArray());
            return expiredKeys.Count;
        }

        return 0;
    }

    private string GetJtiKey(string jti) => $"{_keyPrefix}jti:{jti}";
    private string GetUserTokensKey(string userId) => $"{_userTokensPrefix}{userId}";
    private string GetUserPatternKey(string userId) => $"{_userTokensPrefix}pattern:{userId}";
    private string GetRefreshTokenKey(string refreshToken) => $"{_refreshTokenPrefix}{HashToken(refreshToken)}";

    private static string HashToken(string token)
    {
        // Use SHA256 hash to avoid storing sensitive refresh tokens directly
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hash);
    }
}

/// <summary>
/// In-memory fallback token revocation service
/// </summary>
public class InMemoryTokenRevocationService : ITokenRevocationService
{
    private readonly Dictionary<string, RevokedTokenInfo> _revokedTokens = new();
    private readonly Dictionary<string, HashSet<string>> _userTokens = new();
    private readonly HashSet<string> _revokedRefreshTokens = new();
    private readonly object _lock = new();
    private readonly ILogger<InMemoryTokenRevocationService> _logger;

    public InMemoryTokenRevocationService(ILogger<InMemoryTokenRevocationService> logger)
    {
        _logger = logger;
    }

    public Task RevokeTokenAsync(string jti, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
    {
        var request = new TokenRevocationRequest
        {
            Jti = jti,
            Reason = TokenRevocationReason.UserLogout,
            TokenExpiry = expiry
        };

        return RevokeTokenAsync(request, cancellationToken);
    }

    public Task RevokeTokenAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var revokedInfo = new RevokedTokenInfo
            {
                Jti = request.Jti,
                UserId = request.UserId,
                RevokedAt = DateTime.UtcNow,
                Reason = request.Reason,
                Details = request.Details,
                RevokedFromIp = request.RevokedFromIp,
                RevokedFromUserAgent = request.RevokedFromUserAgent,
                ExpiresAt = DateTime.UtcNow.Add(request.TokenExpiry ?? TimeSpan.FromDays(30))
            };

            _revokedTokens[request.Jti] = revokedInfo;

            if (!_userTokens.ContainsKey(request.UserId))
            {
                _userTokens[request.UserId] = new HashSet<string>();
            }
            _userTokens[request.UserId].Add(request.Jti);

            _logger.LogInformation(
                "Token revoked (in-memory): JTI={Jti}, UserId={UserId}, Reason={Reason}",
                request.Jti, request.UserId, request.Reason);
        }

        return Task.CompletedTask;
    }

    public Task RevokeUserTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_userTokens.TryGetValue(userId, out var userTokens))
            {
                var revokedCount = userTokens.Count;
                
                foreach (var jti in userTokens)
                {
                    if (_revokedTokens.TryGetValue(jti, out var existingInfo))
                    {
                        // Update existing revoked token
                        existingInfo.Reason = TokenRevocationReason.AdminRevocation;
                        existingInfo.RevokedAt = DateTime.UtcNow;
                    }
                }

                _logger.LogInformation(
                    "All tokens revoked for user (in-memory): UserId={UserId}, Count={Count}",
                    userId, revokedCount);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_revokedTokens.ContainsKey(jti));
        }
    }

    public Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _revokedRefreshTokens.Add(refreshToken);
            _logger.LogInformation("Refresh token revoked (in-memory)");
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsRefreshTokenRevokedAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_revokedRefreshTokens.Contains(refreshToken));
        }
    }

    public Task<IEnumerable<RevokedTokenInfo>> GetRevokedTokensAsync(string userId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_userTokens.TryGetValue(userId, out var userTokens))
            {
                var revokedTokens = userTokens
                    .Where(jti => _revokedTokens.ContainsKey(jti))
                    .Select(jti => _revokedTokens[jti])
                    .OrderByDescending(t => t.RevokedAt)
                    .ToList();

                return Task.FromResult<IEnumerable<RevokedTokenInfo>>(revokedTokens);
            }

            return Task.FromResult(Enumerable.Empty<RevokedTokenInfo>());
        }
    }

    public Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var expiredTokens = _revokedTokens
                .Where(kvp => kvp.Value.ExpiresAt < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var jti in expiredTokens)
            {
                var revokedInfo = _revokedTokens[jti];
                _revokedTokens.Remove(jti);

                if (_userTokens.TryGetValue(revokedInfo.UserId, out var userTokens))
                {
                    userTokens.Remove(jti);
                    if (userTokens.Count == 0)
                    {
                        _userTokens.Remove(revokedInfo.UserId);
                    }
                }
            }

            _logger.LogInformation("Token cleanup completed (in-memory): {DeletedCount} expired tokens removed", expiredTokens.Count);
        }

        return Task.CompletedTask;
    }
}