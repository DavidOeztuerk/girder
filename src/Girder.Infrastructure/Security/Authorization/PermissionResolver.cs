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
        InitializeGirderPermissions();
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

    private void InitializeGirderPermissions()
    {
        // User permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.USER_READ,
                Description = "Read user information",
                ResourceType = GirderResources.USER,
                Actions = new List<string> { GirderActions.READ },
                Category = PermissionCategory.Standard,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.USER_UPDATE,
                Description = "Update user information",
                ResourceType = GirderResources.USER,
                Actions = new List<string> { GirderActions.UPDATE },
                Category = PermissionCategory.Standard,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.USER_DELETE,
                Description = "Delete user account",
                ResourceType = GirderResources.USER,
                Actions = new List<string> { GirderActions.DELETE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.USER_ADMIN,
                Description = "Administrative access to user management",
                ResourceType = GirderResources.USER,
                Actions = new List<string> { GirderActions.READ, GirderActions.UPDATE, GirderActions.DELETE, GirderActions.ADMIN },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            }
        });

        // Skill permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.SKILL_READ,
                Description = "Read skill information",
                ResourceType = GirderResources.SKILL,
                Actions = new List<string> { GirderActions.READ },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.SKILL_CREATE,
                Description = "Create new skills",
                ResourceType = GirderResources.SKILL,
                Actions = new List<string> { GirderActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.SKILL_UPDATE,
                Description = "Update skill information",
                ResourceType = GirderResources.SKILL,
                Actions = new List<string> { GirderActions.UPDATE },
                Category = PermissionCategory.Standard,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.SKILL_DELETE,
                Description = "Delete skills",
                ResourceType = GirderResources.SKILL,
                Actions = new List<string> { GirderActions.DELETE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.SKILL_ADMIN,
                Description = "Administrative access to skill management",
                ResourceType = GirderResources.SKILL,
                Actions = new List<string> { GirderActions.READ, GirderActions.CREATE, GirderActions.UPDATE, GirderActions.DELETE, GirderActions.ADMIN },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            }
        });

        // Match permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.MATCH_READ,
                Description = "Read match information",
                ResourceType = GirderResources.MATCH,
                Actions = new List<string> { GirderActions.READ },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in match"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.MATCH_CREATE,
                Description = "Create match requests",
                ResourceType = GirderResources.MATCH,
                Actions = new List<string> { GirderActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.MATCH_UPDATE,
                Description = "Update match information",
                ResourceType = GirderResources.MATCH,
                Actions = new List<string> { GirderActions.UPDATE },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in match"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.MATCH_ACCEPT,
                Description = "Accept match requests",
                ResourceType = GirderResources.MATCH,
                Actions = new List<string> { GirderActions.ACCEPT },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is target of match request"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.MATCH_REJECT,
                Description = "Reject match requests",
                ResourceType = GirderResources.MATCH,
                Actions = new List<string> { GirderActions.REJECT },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is target of match request"
            }
        });

        // Appointment permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.APPOINTMENT_READ,
                Description = "Read appointment information",
                ResourceType = GirderResources.APPOINTMENT,
                Actions = new List<string> { GirderActions.READ },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in appointment"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.APPOINTMENT_CREATE,
                Description = "Create appointments",
                ResourceType = GirderResources.APPOINTMENT,
                Actions = new List<string> { GirderActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.APPOINTMENT_UPDATE,
                Description = "Update appointment information",
                ResourceType = GirderResources.APPOINTMENT,
                Actions = new List<string> { GirderActions.UPDATE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.APPOINTMENT_DELETE,
                Description = "Delete appointments",
                ResourceType = GirderResources.APPOINTMENT,
                Actions = new List<string> { GirderActions.DELETE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.APPOINTMENT_JOIN,
                Description = "Join appointments",
                ResourceType = GirderResources.APPOINTMENT,
                Actions = new List<string> { GirderActions.JOIN },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is invited to appointment"
            }
        });

        // Videocall permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.VIDEOCALL_CREATE,
                Description = "Create video calls",
                ResourceType = GirderResources.VIDEOCALL,
                Actions = new List<string> { GirderActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.VIDEOCALL_JOIN,
                Description = "Join video calls",
                ResourceType = GirderResources.VIDEOCALL,
                Actions = new List<string> { GirderActions.JOIN },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in videocall"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.VIDEOCALL_MODERATE,
                Description = "Moderate video calls",
                ResourceType = GirderResources.VIDEOCALL,
                Actions = new List<string> { GirderActions.MODERATE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.VIDEOCALL_RECORD,
                Description = "Record video calls",
                ResourceType = GirderResources.VIDEOCALL,
                Actions = new List<string> { GirderActions.RECORD },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            }
        });

        // System permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.SYSTEM_ADMIN,
                Description = "System administration access",
                ResourceType = GirderResources.SYSTEM,
                Actions = new List<string> { GirderActions.ADMIN },
                Category = PermissionCategory.System,
                MinimumRole = "SuperAdmin"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.SYSTEM_MONITOR,
                Description = "System monitoring access",
                ResourceType = GirderResources.SYSTEM,
                Actions = new List<string> { GirderActions.MONITOR },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.SYSTEM_BACKUP,
                Description = "System backup operations",
                ResourceType = GirderResources.SYSTEM,
                Actions = new List<string> { GirderActions.BACKUP },
                Category = PermissionCategory.System,
                MinimumRole = "Admin"
            }
        });

        // Notification permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = GirderPermissions.NOTIFICATION_SEND,
                Description = "Send notifications",
                ResourceType = GirderResources.NOTIFICATION,
                Actions = new List<string> { GirderActions.SEND },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = GirderPermissions.NOTIFICATION_ADMIN,
                Description = "Administrative access to notification system",
                ResourceType = GirderResources.NOTIFICATION,
                Actions = new List<string> { GirderActions.ADMIN },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            }
        });

        _logger.LogInformation("Initialized {Count} permission definitions for Girder", 
            _resourcePermissions.Values.SelectMany(p => p).Count());
    }
}