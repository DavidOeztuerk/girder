using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using Infrastructure.Security;

namespace Infrastructure.Authorization;

/// <summary>
/// Requirement for permission-based authorization
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public string? Resource { get; }

    public PermissionRequirement(string permission, string? resource = null)
    {
        Permission = permission;
        Resource = resource;
    }
}

/// <summary>
/// Handler for permission-based authorization
/// </summary>
public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            _logger.LogDebug("User is not authenticated");
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var userPermissions = GetUserPermissions(context.User);

        // Check if user has the required permission
        if (HasPermission(userPermissions, requirement.Permission, requirement.Resource))
        {
            _logger.LogDebug(
                "User {UserId} has permission {Permission}",
                userId,
                requirement.Permission);
            
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning(
                "User {UserId} does not have permission {Permission}",
                userId,
                requirement.Permission);
        }

        return Task.CompletedTask;
    }

    private List<string> GetUserPermissions(ClaimsPrincipal user)
    {
        var permissions = new List<string>();

        // Get permissions from claims
        var permissionClaims = user.FindAll("permission");
        permissions.AddRange(permissionClaims.Select(c => c.Value));

        // Get roles and add their permissions
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
        if (roles.Any())
        {
            var rolePermissions = RolePermissions.GetPermissionsForRoles(roles);
            permissions.AddRange(rolePermissions);
        }

        return permissions.Distinct().ToList();
    }

    private bool HasPermission(List<string> userPermissions, string requiredPermission, string? resource)
    {
        // Direct permission check
        if (userPermissions.Contains(requiredPermission))
            return true;

        // Check for resource-specific permission
        if (!string.IsNullOrEmpty(resource))
        {
            var resourcePermission = $"{requiredPermission}:{resource}";
            if (userPermissions.Contains(resourcePermission))
                return true;
        }

        // Check for wildcard permissions (e.g., "users:*" matches "users:create")
        var permissionParts = requiredPermission.Split(':');
        if (permissionParts.Length == 2)
        {
            var wildcardPermission = $"{permissionParts[0]}:*";
            if (userPermissions.Contains(wildcardPermission))
                return true;
        }

        // Check for system:manage_all (super admin)
        if (userPermissions.Contains(Permissions.SystemManageAll))
            return true;

        return false;
    }
}

/// <summary>
/// Policy provider for dynamic permission-based policies
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;
    private const string PERMISSION_PREFIX = "Permission:";

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallbackPolicyProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallbackPolicyProvider.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PERMISSION_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            var permission = policyName.Substring(PERMISSION_PREFIX.Length);
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }
}

/// <summary>
/// Attribute for permission-based authorization
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute
{
    private const string PERMISSION_PREFIX = "Permission:";

    public RequirePermissionAttribute(string permission) : base()
    {
        Permission = permission;
        Policy = $"{PERMISSION_PREFIX}{permission}";
    }

    public string Permission { get; }
}

/// <summary>
/// Extension methods for configuring permission-based authorization
/// </summary>
public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

        return services;
    }

    public static AuthorizationOptions AddPermissionPolicies(this AuthorizationOptions options)
    {
        // Add common permission policies
        options.AddPolicy("AdminOnly", policy =>
            policy.RequireRole(Roles.Admin, Roles.SuperAdmin));

        options.AddPolicy("ModeratorOnly", policy =>
            policy.RequireRole(Roles.Moderator, Roles.Admin, Roles.SuperAdmin));

        options.AddPolicy("SuperAdminOnly", policy =>
            policy.RequireRole(Roles.SuperAdmin));

        // Add specific permission policies
        options.AddPolicy("CanManageUsers", policy =>
            policy.AddRequirements(new PermissionRequirement(Permissions.UsersManageRoles)));

        options.AddPolicy("CanViewAllUsers", policy =>
            policy.AddRequirements(new PermissionRequirement(Permissions.UsersViewAll)));

        options.AddPolicy("CanManageSkills", policy =>
            policy.AddRequirements(new PermissionRequirement(Permissions.SkillsManageCategories)));

        options.AddPolicy("CanModerateContent", policy =>
            policy.AddRequirements(new PermissionRequirement(Permissions.ContentModerate)));

        options.AddPolicy("CanAccessAdmin", policy =>
            policy.AddRequirements(new PermissionRequirement(Permissions.AdminAccessDashboard)));

        return options;
    }
}