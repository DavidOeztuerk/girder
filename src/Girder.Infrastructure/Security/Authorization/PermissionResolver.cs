using Microsoft.Extensions.Logging;

namespace Infrastructure.Security.Authorization;

/// <summary>
/// Default permission resolver for Skillswap application
/// </summary>
public class PermissionResolver : IPermissionResolver
{
    private readonly Dictionary<string, List<PermissionDefinition>> _resourcePermissions = new();
    private readonly Dictionary<string, Dictionary<string, List<PermissionDefinition>>> _actionPermissions = new();
    private readonly ILogger<PermissionResolver> _logger;

    public PermissionResolver(ILogger<PermissionResolver> logger)
    {
        _logger = logger;
        InitializeSkillswapPermissions();
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

    private void InitializeSkillswapPermissions()
    {
        // User permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = SkillswapPermissions.USER_READ,
                Description = "Read user information",
                ResourceType = SkillswapResources.USER,
                Actions = new List<string> { SkillswapActions.READ },
                Category = PermissionCategory.Standard,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.USER_UPDATE,
                Description = "Update user information",
                ResourceType = SkillswapResources.USER,
                Actions = new List<string> { SkillswapActions.UPDATE },
                Category = PermissionCategory.Standard,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.USER_DELETE,
                Description = "Delete user account",
                ResourceType = SkillswapResources.USER,
                Actions = new List<string> { SkillswapActions.DELETE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.USER_ADMIN,
                Description = "Administrative access to user management",
                ResourceType = SkillswapResources.USER,
                Actions = new List<string> { SkillswapActions.READ, SkillswapActions.UPDATE, SkillswapActions.DELETE, SkillswapActions.ADMIN },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            }
        });

        // Skill permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SKILL_READ,
                Description = "Read skill information",
                ResourceType = SkillswapResources.SKILL,
                Actions = new List<string> { SkillswapActions.READ },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SKILL_CREATE,
                Description = "Create new skills",
                ResourceType = SkillswapResources.SKILL,
                Actions = new List<string> { SkillswapActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SKILL_UPDATE,
                Description = "Update skill information",
                ResourceType = SkillswapResources.SKILL,
                Actions = new List<string> { SkillswapActions.UPDATE },
                Category = PermissionCategory.Standard,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SKILL_DELETE,
                Description = "Delete skills",
                ResourceType = SkillswapResources.SKILL,
                Actions = new List<string> { SkillswapActions.DELETE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SKILL_ADMIN,
                Description = "Administrative access to skill management",
                ResourceType = SkillswapResources.SKILL,
                Actions = new List<string> { SkillswapActions.READ, SkillswapActions.CREATE, SkillswapActions.UPDATE, SkillswapActions.DELETE, SkillswapActions.ADMIN },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            }
        });

        // Match permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = SkillswapPermissions.MATCH_READ,
                Description = "Read match information",
                ResourceType = SkillswapResources.MATCH,
                Actions = new List<string> { SkillswapActions.READ },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in match"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.MATCH_CREATE,
                Description = "Create match requests",
                ResourceType = SkillswapResources.MATCH,
                Actions = new List<string> { SkillswapActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.MATCH_UPDATE,
                Description = "Update match information",
                ResourceType = SkillswapResources.MATCH,
                Actions = new List<string> { SkillswapActions.UPDATE },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in match"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.MATCH_ACCEPT,
                Description = "Accept match requests",
                ResourceType = SkillswapResources.MATCH,
                Actions = new List<string> { SkillswapActions.ACCEPT },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is target of match request"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.MATCH_REJECT,
                Description = "Reject match requests",
                ResourceType = SkillswapResources.MATCH,
                Actions = new List<string> { SkillswapActions.REJECT },
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
                Name = SkillswapPermissions.APPOINTMENT_READ,
                Description = "Read appointment information",
                ResourceType = SkillswapResources.APPOINTMENT,
                Actions = new List<string> { SkillswapActions.READ },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in appointment"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.APPOINTMENT_CREATE,
                Description = "Create appointments",
                ResourceType = SkillswapResources.APPOINTMENT,
                Actions = new List<string> { SkillswapActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.APPOINTMENT_UPDATE,
                Description = "Update appointment information",
                ResourceType = SkillswapResources.APPOINTMENT,
                Actions = new List<string> { SkillswapActions.UPDATE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.APPOINTMENT_DELETE,
                Description = "Delete appointments",
                ResourceType = SkillswapResources.APPOINTMENT,
                Actions = new List<string> { SkillswapActions.DELETE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.APPOINTMENT_JOIN,
                Description = "Join appointments",
                ResourceType = SkillswapResources.APPOINTMENT,
                Actions = new List<string> { SkillswapActions.JOIN },
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
                Name = SkillswapPermissions.VIDEOCALL_CREATE,
                Description = "Create video calls",
                ResourceType = SkillswapResources.VIDEOCALL,
                Actions = new List<string> { SkillswapActions.CREATE },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.VIDEOCALL_JOIN,
                Description = "Join video calls",
                ResourceType = SkillswapResources.VIDEOCALL,
                Actions = new List<string> { SkillswapActions.JOIN },
                Category = PermissionCategory.Conditional,
                IsConditional = true,
                Condition = "user is participant in videocall"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.VIDEOCALL_MODERATE,
                Description = "Moderate video calls",
                ResourceType = SkillswapResources.VIDEOCALL,
                Actions = new List<string> { SkillswapActions.MODERATE },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.VIDEOCALL_RECORD,
                Description = "Record video calls",
                ResourceType = SkillswapResources.VIDEOCALL,
                Actions = new List<string> { SkillswapActions.RECORD },
                Category = PermissionCategory.Owner,
                IsOwnerPermission = true
            }
        });

        // System permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SYSTEM_ADMIN,
                Description = "System administration access",
                ResourceType = SkillswapResources.SYSTEM,
                Actions = new List<string> { SkillswapActions.ADMIN },
                Category = PermissionCategory.System,
                MinimumRole = "SuperAdmin"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SYSTEM_MONITOR,
                Description = "System monitoring access",
                ResourceType = SkillswapResources.SYSTEM,
                Actions = new List<string> { SkillswapActions.MONITOR },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.SYSTEM_BACKUP,
                Description = "System backup operations",
                ResourceType = SkillswapResources.SYSTEM,
                Actions = new List<string> { SkillswapActions.BACKUP },
                Category = PermissionCategory.System,
                MinimumRole = "Admin"
            }
        });

        // Notification permissions
        RegisterPermissions(new[]
        {
            new PermissionDefinition
            {
                Name = SkillswapPermissions.NOTIFICATION_SEND,
                Description = "Send notifications",
                ResourceType = SkillswapResources.NOTIFICATION,
                Actions = new List<string> { SkillswapActions.SEND },
                Category = PermissionCategory.Standard
            },
            new PermissionDefinition
            {
                Name = SkillswapPermissions.NOTIFICATION_ADMIN,
                Description = "Administrative access to notification system",
                ResourceType = SkillswapResources.NOTIFICATION,
                Actions = new List<string> { SkillswapActions.ADMIN },
                Category = PermissionCategory.Administrative,
                MinimumRole = "Admin"
            }
        });

        _logger.LogInformation("Initialized {Count} permission definitions for Skillswap", 
            _resourcePermissions.Values.SelectMany(p => p).Count());
    }
}