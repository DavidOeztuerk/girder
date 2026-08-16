using Girder.Abstractions.Security.Authorization;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using AuthorizationResult = Girder.Abstractions.Security.Authorization.AuthorizationResult;

namespace Girder.InMemory.Security;

/// <summary>
/// In-memory resource authorization service for development/testing
/// </summary>
public class InMemoryResourceAuthorizationService : IResourceAuthorizationService
{
    private readonly Dictionary<string, Dictionary<string, List<string>>> _userPermissions = new();
    private readonly Dictionary<string, string> _resourceOwners = new();
    private readonly IPermissionResolver _permissionResolver;
    private readonly object _lock = new();

    public InMemoryResourceAuthorizationService(IPermissionResolver permissionResolver)
    {
        _permissionResolver = permissionResolver;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(
        System.Security.Claims.ClaimsPrincipal user,
        string resource,
        string action,
        object? resourceData = null,
        CancellationToken cancellationToken = default)
    {
        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return AuthorizationResult.Fail("User ID not found");
        }

        var requiredPermissions = await _permissionResolver.GetRequiredPermissionsAsync(resource, action);

        // Fail closed: if no permissions are defined for this resource/action pair,
        // the combination is unsupported and must not silently authorize.
        if (!requiredPermissions.Any())
        {
            return AuthorizationResult.Fail(
                $"No permissions defined for resource '{resource}' action '{action}'");
        }

        var userPermissions = await GetUserPermissionsAsync(userId, resource, GetResourceId(resourceData) ?? "*", cancellationToken);

        foreach (var permission in requiredPermissions)
        {
            if (!userPermissions.Contains(permission.Name))
            {
                return AuthorizationResult.Fail($"Missing permission: {permission.Name}");
            }
        }

        return AuthorizationResult.Success();
    }

    public Task<bool> IsResourceOwnerAsync(string userId, string resourceType, string resourceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = $"{resourceType}:{resourceId}";
            return Task.FromResult(_resourceOwners.TryGetValue(key, out var owner) && owner == userId);
        }
    }

    public Task<IEnumerable<string>> GetUserPermissionsAsync(string userId, string resourceType, string resourceId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = $"{resourceType}:{resourceId}";
            if (_userPermissions.TryGetValue(userId, out var userResources) &&
                userResources.TryGetValue(key, out var permissions))
            {
                return Task.FromResult(permissions.AsEnumerable());
            }
            return Task.FromResult(Enumerable.Empty<string>());
        }
    }

    public Task GrantPermissionAsync(string userId, string resourceType, string resourceId, string permission, string grantedBy, DateTime? expiresAt = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_userPermissions.ContainsKey(userId))
            {
                _userPermissions[userId] = new Dictionary<string, List<string>>();
            }

            var key = $"{resourceType}:{resourceId}";
            if (!_userPermissions[userId].ContainsKey(key))
            {
                _userPermissions[userId][key] = new List<string>();
            }

            if (!_userPermissions[userId][key].Contains(permission))
            {
                _userPermissions[userId][key].Add(permission);
            }
        }
        return Task.CompletedTask;
    }

    public Task RevokePermissionAsync(string userId, string resourceType, string resourceId, string permission, string revokedBy, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var key = $"{resourceType}:{resourceId}";
            if (_userPermissions.TryGetValue(userId, out var userResources) &&
                userResources.TryGetValue(key, out var permissions))
            {
                permissions.Remove(permission);
            }
        }
        return Task.CompletedTask;
    }

    public Task<bool> HasPermissionAsync(string userId, string permission, string? resourceType = null, string? resourceId = null, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (string.IsNullOrEmpty(resourceType))
            {
                // Check global permissions
                return Task.FromResult(false);
            }

            var key = $"{resourceType}:{resourceId ?? "*"}";
            if (_userPermissions.TryGetValue(userId, out var userResources) &&
                userResources.TryGetValue(key, out var permissions))
            {
                return Task.FromResult(permissions.Contains(permission));
            }
            return Task.FromResult(false);
        }
    }

    public Task<IEnumerable<ResourceAccess>> GetAccessibleResourcesAsync(string userId, string resourceType, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var accessList = new List<ResourceAccess>();

            if (_userPermissions.TryGetValue(userId, out var userResources))
            {
                foreach (var kvp in userResources)
                {
                    var parts = kvp.Key.Split(':');
                    if (parts.Length == 2 && parts[0] == resourceType)
                    {
                        accessList.Add(new ResourceAccess
                        {
                            ResourceType = resourceType,
                            ResourceId = parts[1],
                            Permissions = kvp.Value,
                            GrantedAt = DateTime.UtcNow,
                            GrantedBy = "System"
                        });
                    }
                }
            }

            return Task.FromResult(accessList.AsEnumerable());
        }
    }

    private static string? GetResourceId(object? resourceData)
    {
        return resourceData?.GetType().GetProperty("Id")?.GetValue(resourceData)?.ToString();
    }
}
