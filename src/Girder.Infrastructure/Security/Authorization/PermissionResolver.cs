using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Default permission resolver for Girder application
/// </summary>
public class PermissionResolver : IPermissionResolver
{
    private readonly Dictionary<string, List<PermissionDefinition>> _resourcePermissions = new();
    private readonly Dictionary<string, Dictionary<string, List<PermissionDefinition>>> _actionPermissions = new();
    private readonly ILogger<PermissionResolver> _logger;

    public PermissionResolver(ILogger<PermissionResolver> logger)
    {
        _logger = logger;
    }

    public Task<IEnumerable<PermissionDefinition>> GetRequiredPermissionsAsync(string resourceType, string action)
    {
        try
        {
            if (_actionPermissions.TryGetValue(resourceType, out var resourceActions) &&
                resourceActions.TryGetValue(action, out var permissions))
            {
                return Task.FromResult(permissions.AsEnumerable());
            }

            _logger.LogWarning("No permissions defined for resource {ResourceType} action {Action}", 
                resourceType, action);
            
            return Task.FromResult(Enumerable.Empty<PermissionDefinition>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting required permissions");
            return Task.FromResult(Enumerable.Empty<PermissionDefinition>());
        }
    }

    public Task<IEnumerable<string>> GetOwnerPermissionsAsync(string resourceType)
    {
        try
        {
            if (_resourcePermissions.TryGetValue(resourceType, out var permissions))
            {
                var ownerPermissions = permissions
                    .Where(p => p.IsOwnerPermission)
                    .Select(p => p.Name);
                
                return Task.FromResult(ownerPermissions);
            }

            return Task.FromResult(Enumerable.Empty<string>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting owner permissions");
            return Task.FromResult(Enumerable.Empty<string>());
        }
    }

    public Task<IEnumerable<PermissionDefinition>> GetAvailablePermissionsAsync(string resourceType)
    {
        try
        {
            if (_resourcePermissions.TryGetValue(resourceType, out var permissions))
            {
                return Task.FromResult(permissions.AsEnumerable());
            }

            return Task.FromResult(Enumerable.Empty<PermissionDefinition>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting available permissions");
            return Task.FromResult(Enumerable.Empty<PermissionDefinition>());
        }
    }

    public void RegisterPermission(PermissionDefinition permission)
    {
        try
        {
            // Add to resource permissions
            if (!_resourcePermissions.ContainsKey(permission.ResourceType))
            {
                _resourcePermissions[permission.ResourceType] = new List<PermissionDefinition>();
            }
            _resourcePermissions[permission.ResourceType].Add(permission);

            // Add to action permissions
            if (!_actionPermissions.ContainsKey(permission.ResourceType))
            {
                _actionPermissions[permission.ResourceType] = new Dictionary<string, List<PermissionDefinition>>();
            }

            foreach (var action in permission.Actions)
            {
                if (!_actionPermissions[permission.ResourceType].ContainsKey(action))
                {
                    _actionPermissions[permission.ResourceType][action] = new List<PermissionDefinition>();
                }
                _actionPermissions[permission.ResourceType][action].Add(permission);
            }

            _logger.LogDebug("Registered permission: {Permission} for {ResourceType}", 
                permission.Name, permission.ResourceType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering permission: {Permission}", permission.Name);
        }
    }

    public void RegisterPermissions(IEnumerable<PermissionDefinition> permissions)
    {
        foreach (var permission in permissions)
        {
            RegisterPermission(permission);
        }
    }

}
