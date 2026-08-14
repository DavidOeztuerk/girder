namespace Infrastructure.Security;

/// <summary>
/// Maps roles to the permissions they implicitly grant.
/// Implements hierarchical role inheritance.
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// Defines the role hierarchy with inheritance
    /// </summary>
    private static readonly Dictionary<string, string[]> _roleInheritance = new()
    {
        [Roles.SuperAdmin] = [Roles.Admin],
        [Roles.Admin] = [Roles.Moderator],
        [Roles.Moderator] = [Roles.User],
        [Roles.User] = []
    };

    private static readonly IReadOnlyDictionary<string, string[]> _rolePermissions =
        new Dictionary<string, string[]>
        {
            [Roles.SuperAdmin] =
            [
                // System exclusive permissions
                Permissions.SystemManageAll,
                Permissions.SystemManageSettings,
                Permissions.SystemManageIntegrations,
                
                // User management exclusive
                Permissions.UsersCreate,
                Permissions.UsersDelete,
                Permissions.UsersManageRoles,
                
                // Role management exclusive
                Permissions.RolesCreate,
                Permissions.RolesUpdate,
                Permissions.RolesDelete,
                Permissions.RolesView,
                Permissions.PermissionsView,
                Permissions.PermissionsManage,
                
                // Admin management
                Permissions.AdminManageAll
            ],
            
            [Roles.Admin] =
            [
                // System monitoring
                Permissions.SystemViewLogs,
                
                // User management
                Permissions.UsersUpdate,
                Permissions.UsersViewAll,
                Permissions.UsersBlock,
                Permissions.UsersUnblock,
                
                // Skill management
                Permissions.SkillsManageCategories,
                Permissions.SkillsManageProficiency,
                Permissions.SkillsViewAll,
                
                // Appointments management
                Permissions.AppointmentsViewAll,
                Permissions.AppointmentsCancelAny,
                Permissions.AppointmentsManage,
                
                // Matching management
                Permissions.MatchingViewAll,
                Permissions.MatchingManage,
                
                // Messages management
                Permissions.MessagesViewAll,
                
                // Video calls management
                Permissions.VideoCallsManage,
                
                // Reports
                Permissions.ReportsViewAll,
                
                // Admin panel
                Permissions.AdminAccessDashboard,
                Permissions.AdminViewStatistics,

                // Security alerts
                Permissions.SecurityViewAlerts,
                Permissions.SecurityManageAlerts,

                // Verification documents
                Permissions.VerificationsView,
                Permissions.VerificationsReview,

                // Role viewing
                Permissions.RolesView,
                Permissions.PermissionsView
            ],
            
            [Roles.Moderator] =
            [
                // User viewing
                Permissions.UsersRead,
                Permissions.UsersViewReported,
                
                // Content moderation
                Permissions.ContentModerate,
                Permissions.ReportsHandle,
                
                // Skill verification
                Permissions.SkillsVerify,
                
                // Review moderation
                Permissions.ReviewsModerate,
                
                // Moderator panel
                Permissions.ModeratorAccessPanel,
                
                // Profile viewing
                Permissions.ProfileViewAny
            ],
            
            [Roles.User] =
            [
                // Profile management
                Permissions.ProfileViewOwn,
                Permissions.ProfileUpdateOwn,
                
                // User viewing
                Permissions.UsersReadOwn,
                Permissions.UsersUpdateOwn,
                
                // Skill management
                Permissions.SkillsCreateOwn,
                Permissions.SkillsUpdateOwn,
                Permissions.SkillsDeleteOwn,
                
                // Appointments
                Permissions.AppointmentsCreate,
                Permissions.AppointmentsViewOwn,
                Permissions.AppointmentsCancelOwn,
                
                // Matching
                Permissions.MatchingAccess,
                
                // Reviews
                Permissions.ReviewsCreate,
                
                // Messages
                Permissions.MessagesSend,
                Permissions.MessagesViewOwn,
                
                // Video calls
                Permissions.VideoCallsAccess
            ]
        };

    /// <summary>
    /// Get all permissions granted by the specified roles, including inherited permissions.
    /// </summary>
    public static IEnumerable<string> GetPermissionsForRoles(IEnumerable<string> roles)
    {
        var allRoles = new HashSet<string>();
        
        // Collect all roles including inherited ones
        foreach (var role in roles)
        {
            CollectInheritedRoles(role, allRoles);
        }
        
        // Get all permissions for all roles (including inherited)
        return allRoles
            .SelectMany(role => _rolePermissions.TryGetValue(role, out var perms)
                ? perms
                : [])
            .Distinct();
    }
    
    /// <summary>
    /// Recursively collect all inherited roles
    /// </summary>
    private static void CollectInheritedRoles(string role, HashSet<string> collectedRoles)
    {
        if (collectedRoles.Contains(role))
            return;
            
        collectedRoles.Add(role);
        
        if (_roleInheritance.TryGetValue(role, out var inheritedRoles))
        {
            foreach (var inheritedRole in inheritedRoles)
            {
                CollectInheritedRoles(inheritedRole, collectedRoles);
            }
        }
    }
    
    /// <summary>
    /// Get all permissions for a specific role (without inheritance)
    /// </summary>
    public static IEnumerable<string> GetDirectPermissionsForRole(string role)
    {
        return _rolePermissions.TryGetValue(role, out var perms)
            ? perms
            : [];
    }
    
    /// <summary>
    /// Check if a role has a specific permission (including inherited)
    /// </summary>
    public static bool RoleHasPermission(string role, string permission)
    {
        var permissions = GetPermissionsForRoles([role]);
        return permissions.Contains(permission);
    }
}
