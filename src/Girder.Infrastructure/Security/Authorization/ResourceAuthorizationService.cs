using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text.Json;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Redis-based resource authorization service
/// </summary>
public class ResourceAuthorizationService : IResourceAuthorizationService
{
    private readonly IDatabase _database;
    private readonly ILogger<ResourceAuthorizationService> _logger;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IPermissionConditions _conditions;
    private readonly string _keyPrefix;

    // Lua script for atomic permission operations
    private const string GrantPermissionScript = @"
        local permissionKey = KEYS[1]
        local userPermissionsKey = KEYS[2]
        local resourcePermissionsKey = KEYS[3]
        local permissionData = ARGV[1]
        local expiry = tonumber(ARGV[2])
        
        -- Store permission grant
        redis.call('SET', permissionKey, permissionData, 'EX', expiry)
        
        -- Add to user's permissions set
        redis.call('SADD', userPermissionsKey, permissionKey)
        redis.call('EXPIRE', userPermissionsKey, expiry)
        
        -- Add to resource permissions set
        redis.call('SADD', resourcePermissionsKey, permissionKey)
        redis.call('EXPIRE', resourcePermissionsKey, expiry)
        
        return 1
    ";

    private const string RevokePermissionScript = @"
        local permissionKey = KEYS[1]
        local userPermissionsKey = KEYS[2]
        local resourcePermissionsKey = KEYS[3]
        
        -- Remove permission grant
        redis.call('DEL', permissionKey)
        
        -- Remove from user's permissions set
        redis.call('SREM', userPermissionsKey, permissionKey)
        
        -- Remove from resource permissions set
        redis.call('SREM', resourcePermissionsKey, permissionKey)
        
        return 1
    ";

    public ResourceAuthorizationService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<ResourceAuthorizationService> logger,
        IPermissionResolver permissionResolver,
        IPermissionConditions? conditions = null)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _permissionResolver = permissionResolver;
        _conditions = conditions ?? PermissionConditions.None;
        _keyPrefix = "auth:";
    }

    public async Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        string resource,
        string action,
        object? resourceData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return AuthorizationResult.Fail("User ID not found in claims");
            }

            var failures = new List<string>();

            // Get required permissions for the action
            var requiredPermissions = await _permissionResolver.GetRequiredPermissionsAsync(resource, action);

            // Fail closed: if no permissions are defined for this resource/action pair,
            // the combination is unsupported and must not silently authorize.
            if (!requiredPermissions.Any())
            {
                _logger.LogWarning(
                    "No permissions defined for resource {Resource} action {Action} — denying access",
                    resource, action);
                return AuthorizationResult.Fail(
                    $"No permissions defined for resource '{resource}' action '{action}'");
            }

            foreach (var permission in requiredPermissions)
            {
                var hasPermission = await HasPermissionAsync(userId, permission.Name, resource,
                    GetResourceId(resourceData), cancellationToken);

                if (!hasPermission)
                {
                    // Check for conditional permissions
                    if (permission.IsConditional)
                    {
                        var conditionMet = await EvaluatePermissionConditionAsync(
                            permission, userId, resource, resourceData);

                        if (!conditionMet)
                        {
                            failures.Add($"Permission '{permission.Name}' denied: condition not met");
                        }
                    }
                    else
                    {
                        failures.Add($"Missing required permission: {permission.Name}");
                    }
                }
            }

            if (failures.Any())
            {
                _logger.LogWarning(
                    "Authorization failed for user {UserId} on resource {Resource} action {Action}: {Failures}",
                    userId, resource, action, string.Join(", ", failures));

                return AuthorizationResult.Fail(failures);
            }

            _logger.LogDebug("Authorization succeeded for user {UserId} on resource {Resource} action {Action}",
                userId, resource, action);

            return AuthorizationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authorization check");
            return AuthorizationResult.Fail("Authorization check failed due to system error");
        }
    }

    public async Task<bool> IsResourceOwnerAsync(
        string userId,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ownershipKey = GetResourceOwnershipKey(resourceType, resourceId);
            var ownerId = await _database.StringGetAsync(ownershipKey);

            return ownerId.HasValue && ownerId == userId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking resource ownership");
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetUserPermissionsAsync(
        string userId,
        string resourceType,
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userPermissionsKey = GetUserPermissionsKey(userId, resourceType, resourceId);
            var permissionKeys = await _database.SetMembersAsync(userPermissionsKey);

            var permissions = new List<string>();

            foreach (var permissionKey in permissionKeys)
            {
                try
                {
                    var permissionData = await _database.StringGetAsync((string)permissionKey!);
                    if (permissionData.HasValue)
                    {
                        var grant = JsonSerializer.Deserialize<PermissionGrant>((string)permissionData!);
                        if (grant != null && grant.IsActive &&
                            (grant.ExpiresAt == null || grant.ExpiresAt > DateTime.UtcNow))
                        {
                            permissions.Add(grant.Permission);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize permission grant: {Key}", permissionKey);
                }
            }

            // Add ownership permissions if user is owner
            if (await IsResourceOwnerAsync(userId, resourceType, resourceId, cancellationToken))
            {
                var ownerPermissions = await _permissionResolver.GetOwnerPermissionsAsync(resourceType);
                permissions.AddRange(ownerPermissions);
            }

            return permissions.Distinct();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user permissions");
            return Enumerable.Empty<string>();
        }
    }

    public async Task GrantPermissionAsync(
        string userId,
        string resourceType,
        string resourceId,
        string permission,
        string grantedBy,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var grant = new PermissionGrant
            {
                UserId = userId,
                ResourceType = resourceType,
                ResourceId = resourceId,
                Permission = permission,
                GrantedBy = grantedBy,
                ExpiresAt = expiresAt,
                GrantedAt = DateTime.UtcNow,
                IsActive = true
            };

            var permissionKey = GetPermissionKey(userId, resourceType, resourceId, permission);
            var userPermissionsKey = GetUserPermissionsKey(userId, resourceType, resourceId);
            var resourcePermissionsKey = GetResourcePermissionsKey(resourceType, resourceId);

            var permissionData = JsonSerializer.Serialize(grant);
            var expiry = (long)(expiresAt?.Subtract(DateTime.UtcNow).TotalSeconds ?? TimeSpan.FromDays(365).TotalSeconds);

            await _database.ScriptEvaluateAsync(
                GrantPermissionScript,
                new RedisKey[] { permissionKey, userPermissionsKey, resourcePermissionsKey },
                new RedisValue[] { permissionData, expiry }
            );

            _logger.LogInformation(
                "Permission granted: {Permission} to user {UserId} for {ResourceType}:{ResourceId} by {GrantedBy}",
                permission, userId, resourceType, resourceId, grantedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to grant permission");
            throw;
        }
    }

    public async Task RevokePermissionAsync(
        string userId,
        string resourceType,
        string resourceId,
        string permission,
        string revokedBy,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var permissionKey = GetPermissionKey(userId, resourceType, resourceId, permission);
            var userPermissionsKey = GetUserPermissionsKey(userId, resourceType, resourceId);
            var resourcePermissionsKey = GetResourcePermissionsKey(resourceType, resourceId);

            await _database.ScriptEvaluateAsync(
                RevokePermissionScript,
                new RedisKey[] { permissionKey, userPermissionsKey, resourcePermissionsKey },
                Array.Empty<RedisValue>()
            );

            _logger.LogInformation(
                "Permission revoked: {Permission} from user {UserId} for {ResourceType}:{ResourceId} by {RevokedBy}",
                permission, userId, resourceType, resourceId, revokedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke permission");
            throw;
        }
    }

    public async Task<bool> HasPermissionAsync(
        string userId,
        string permission,
        string? resourceType = null,
        string? resourceId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check global permission first
            if (string.IsNullOrEmpty(resourceType))
            {
                var globalPermissions = await GetUserGlobalPermissionsAsync(userId);
                return globalPermissions.Contains(permission);
            }

            // Check resource-specific permission
            var resourcePermissions = await GetUserPermissionsAsync(userId, resourceType, resourceId ?? "*", cancellationToken);
            return resourcePermissions.Contains(permission);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking permission");
            return false;
        }
    }

    public async Task<IEnumerable<ResourceAccess>> GetAccessibleResourcesAsync(
        string userId,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var accessList = new List<ResourceAccess>();
            var userPattern = GetUserPermissionsPattern(userId, resourceType);

            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: userPattern);

            foreach (var key in keys)
            {
                try
                {
                    var permissionKeys = await _database.SetMembersAsync(key);
                    var resourceId = ExtractResourceIdFromKey(key!);

                    var permissions = new List<string>();
                    var grantedAt = DateTime.MaxValue;
                    var expiresAt = (DateTime?)null;
                    var grantedBy = "";

                    foreach (var permissionKey in permissionKeys)
                    {
                        var permissionData = await _database.StringGetAsync((string)permissionKey!);
                        if (permissionData.HasValue)
                        {
                            var grant = JsonSerializer.Deserialize<PermissionGrant>((string)permissionData!);
                            if (grant != null && grant.IsActive &&
                                (grant.ExpiresAt == null || grant.ExpiresAt > DateTime.UtcNow))
                            {
                                permissions.Add(grant.Permission);
                                if (grant.GrantedAt < grantedAt)
                                {
                                    grantedAt = grant.GrantedAt;
                                    grantedBy = grant.GrantedBy;
                                }
                                if (grant.ExpiresAt.HasValue)
                                {
                                    expiresAt = expiresAt == null ? grant.ExpiresAt :
                                        grant.ExpiresAt < expiresAt ? grant.ExpiresAt : expiresAt;
                                }
                            }
                        }
                    }

                    if (permissions.Any())
                    {
                        var isOwner = await IsResourceOwnerAsync(userId, resourceType, resourceId, cancellationToken);

                        accessList.Add(new ResourceAccess
                        {
                            ResourceType = resourceType,
                            ResourceId = resourceId,
                            Permissions = permissions,
                            GrantedAt = grantedAt,
                            ExpiresAt = expiresAt,
                            GrantedBy = grantedBy,
                            IsOwner = isOwner
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing resource access for key: {Key}", key);
                }
            }

            return accessList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting accessible resources");
            return Enumerable.Empty<ResourceAccess>();
        }
    }

    private Task<IEnumerable<string>> GetUserGlobalPermissionsAsync(string userId)
    {
        // Global permissions require a DB-backed role/permission lookup (PermissionRepository in UserService).
        // This Redis-based subsystem has no authoritative source for role-to-permission mapping,
        // so we return empty (fail-closed) until a real integration is wired.
        return Task.FromResult(Enumerable.Empty<string>());
    }

    private Task<bool> EvaluatePermissionConditionAsync(
        PermissionDefinition permission,
        string userId,
        string resource,
        object? resourceData)
    {
        if (string.IsNullOrEmpty(permission.Condition) || resourceData is null)
        {
            return Task.FromResult(false);
        }

        // An undeclared condition denies, but silently denying forever looks
        // exactly like a working rule, so say it out loud once per request.
        if (!_conditions.Knows(permission.Condition))
        {
            _logger.LogWarning(
                "Permission {Permission} names condition {Condition}, which is not declared. "
                + "Declare it with AddPermissionConditions; until then the permission is denied.",
                permission.Name, permission.Condition);

            return Task.FromResult(false);
        }

        var context = new PermissionConditionContext(userId, resource, resourceData);
        return Task.FromResult(_conditions.IsSatisfied(permission.Condition, context));
    }

    private static string? GetResourceId(object? resourceData)
    {
        return resourceData?.GetType().GetProperty("Id")?.GetValue(resourceData)?.ToString();
    }

    private string GetPermissionKey(string userId, string resourceType, string resourceId, string permission) =>
        $"{_keyPrefix}permission:{userId}:{resourceType}:{resourceId}:{permission}";

    private string GetUserPermissionsKey(string userId, string resourceType, string resourceId) =>
        $"{_keyPrefix}user:{userId}:{resourceType}:{resourceId}";

    private string GetResourcePermissionsKey(string resourceType, string resourceId) =>
        $"{_keyPrefix}resource:{resourceType}:{resourceId}";

    private string GetResourceOwnershipKey(string resourceType, string resourceId) =>
        $"{_keyPrefix}owner:{resourceType}:{resourceId}";

    private string GetUserPermissionsPattern(string userId, string resourceType) =>
        $"{_keyPrefix}user:{userId}:{resourceType}:*";

    private static string ExtractResourceIdFromKey(string key)
    {
        var parts = key.Split(':');
        return parts.Length >= 5 ? parts[4] : "";
    }
}