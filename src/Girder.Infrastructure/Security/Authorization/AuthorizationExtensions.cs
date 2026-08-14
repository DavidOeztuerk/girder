using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Infrastructure.Security.Authorization;

/// <summary>
/// Extension methods for authorization services
/// </summary>
public static class AuthorizationExtensions
{
    /// <summary>
    /// Add resource-based authorization services
    /// </summary>
    public static IServiceCollection AddResourceAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            // Redis-based authorization
            services.AddSingleton<IResourceAuthorizationService, ResourceAuthorizationService>();
        }
        else
        {
            // In-memory fallback
            services.AddSingleton<IResourceAuthorizationService, InMemoryResourceAuthorizationService>();
        }

        // Register permission resolver
        services.AddSingleton<IPermissionResolver, PermissionResolver>();

        // Register authorization handlers
        services.AddScoped<IAuthorizationHandler, ResourceAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, OwnershipAuthorizationHandler>();

        // Add authorization policies
        services.AddAuthorization(options =>
        {
            // Resource-based policies
            options.AddPolicy("ResourceRead", policy =>
                policy.Requirements.Add(new ResourceRequirement(SkillswapActions.READ)));

            options.AddPolicy("ResourceWrite", policy =>
                policy.Requirements.Add(new ResourceRequirement(SkillswapActions.UPDATE)));

            options.AddPolicy("ResourceDelete", policy =>
                policy.Requirements.Add(new ResourceRequirement(SkillswapActions.DELETE)));

            options.AddPolicy("ResourceOwner", policy =>
                policy.Requirements.Add(new OwnershipRequirement()));

            // Admin policies
            options.AddPolicy("AdminOnly", policy =>
                policy.RequireRole("Admin"));

            options.AddPolicy("SuperAdminOnly", policy =>
                policy.RequireRole("SuperAdmin"));

            // Service-specific policies
            options.AddPolicy("UserManagement", policy =>
                policy.Requirements.Add(new ResourceRequirement(SkillswapActions.ADMIN, SkillswapResources.USER)));

            options.AddPolicy("SkillManagement", policy =>
                policy.Requirements.Add(new ResourceRequirement(SkillswapActions.ADMIN, SkillswapResources.SKILL)));

            options.AddPolicy("SystemAccess", policy =>
                policy.Requirements.Add(new ResourceRequirement(SkillswapActions.MONITOR, SkillswapResources.SYSTEM)));
        });

        return services;
    }

    /// <summary>
    /// Add authorization middleware
    /// </summary>
    public static IServiceCollection AddAuthorizationMiddleware(this IServiceCollection services)
    {
        services.AddTransient<ResourceAuthorizationMiddleware>();
        return services;
    }
}

/// <summary>
/// Resource authorization requirement
/// </summary>
public class ResourceRequirement : IAuthorizationRequirement
{
    public string Action { get; }
    public string? ResourceType { get; }

    public ResourceRequirement(string action, string? resourceType = null)
    {
        Action = action;
        ResourceType = resourceType;
    }
}

/// <summary>
/// Ownership authorization requirement
/// </summary>
public class OwnershipRequirement : IAuthorizationRequirement
{
    public string? ResourceType { get; }

    public OwnershipRequirement(string? resourceType = null)
    {
        ResourceType = resourceType;
    }
}

/// <summary>
/// Resource authorization handler.
/// Supports both MVC (AuthorizationFilterContext) and Minimal API (HttpContext) resources.
/// NOTE: GetResourceDataFromContext returns null because route data alone
/// (e.g. { Id = "..." }) lacks the domain fields (RequesterId, TargetUserId,
/// OrganizerUserId, ParticipantUserId, HostUserId) that EvaluatePermissionConditionAsync
/// requires. This means conditional permissions (IsConditional=true) will fail-closed
/// at the handler level until callers supply real domain objects as the authorization resource.
/// Non-conditional permissions and ownership checks work as expected.
/// </summary>
public class ResourceAuthorizationHandler : AuthorizationHandler<ResourceRequirement>
{
    private readonly IResourceAuthorizationService _authorizationService;

    public ResourceAuthorizationHandler(IResourceAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceRequirement requirement)
    {
        var resourceType = requirement.ResourceType ?? GetResourceTypeFromContext(context);
        var resourceData = GetResourceDataFromContext(context);

        if (string.IsNullOrEmpty(resourceType))
        {
            context.Fail();
            return;
        }

        var result = await _authorizationService.AuthorizeAsync(
            context.User,
            resourceType,
            requirement.Action,
            resourceData);

        if (result.Succeeded)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }

    internal static string? GetResourceTypeFromContext(AuthorizationHandlerContext context)
    {
        // MVC path: extract controller name from AuthorizationFilterContext
        if (context.Resource is AuthorizationFilterContext filterContext)
        {
            if (filterContext.RouteData.Values.TryGetValue("controller", out var controller))
            {
                return controller?.ToString();
            }
        }

        // Minimal API path: extract from HttpContext endpoint metadata or route values
        var httpContext = context.Resource as HttpContext;
        if (httpContext != null)
        {
            return ResolveResourceTypeFromHttpContext(httpContext);
        }

        return null;
    }

    internal static string? ResolveResourceTypeFromHttpContext(HttpContext httpContext)
    {
        // 1. Check for explicit ResourceAuthorizeAttribute metadata first
        var endpoint = httpContext.GetEndpoint();
        var resourceAttr = endpoint?.Metadata.GetMetadata<ResourceAuthorizeAttribute>();
        if (!string.IsNullOrEmpty(resourceAttr?.Resource))
        {
            return resourceAttr.Resource;
        }

        // 2. Infer from typed route-value names (e.g. appointmentId → Appointment).
        //    This is more specific than path segments and handles multi-resource routes
        //    like /users/calendar/{userId}/sync/{appointmentId} correctly.
        var routeValueType = InferResourceTypeFromRouteValues(httpContext.Request.RouteValues);

        // 3. Infer from path segments (e.g. /appointments/... → Appointment)
        string? pathType = null;
        var path = httpContext.Request.Path.Value;
        if (!string.IsNullOrEmpty(path))
        {
            pathType = InferResourceTypeFromPath(path);
        }

        // 4. Reconcile: route-value inference wins; conflict → fail closed
        if (routeValueType != null && pathType != null
            && !string.Equals(routeValueType, pathType, StringComparison.Ordinal))
        {
            // Conflict between route-value-derived type and path-derived type → fail closed
            return null;
        }

        // Route-value type takes priority (more specific), then path type
        return routeValueType ?? pathType;
    }

    /// <summary>
    /// Infers a resource type from typed route-value parameter names.
    /// Maps specific params like "appointmentId" → Appointment, "matchId" → Match.
    /// Generic "id" and "userId" are excluded (too ambiguous).
    /// Returns null if no specific typed param found, or if multiple distinct types found.
    /// </summary>
    internal static string? InferResourceTypeFromRouteValues(IReadOnlyDictionary<string, object?> routeValues)
    {
        string? inferredType = null;

        foreach (var kvp in routeValues)
        {
            if (kvp.Value == null) continue;
            var stringValue = kvp.Value.ToString();
            if (string.IsNullOrEmpty(stringValue)) continue;

            var mappedType = MapRouteParamToResourceType(kvp.Key);
            if (mappedType == null) continue;

            if (inferredType == null)
            {
                inferredType = mappedType;
            }
            else if (!string.Equals(inferredType, mappedType, StringComparison.Ordinal))
            {
                // Multiple distinct resource types from route params → ambiguous → fail closed
                return null;
            }
        }

        return inferredType;
    }

    /// <summary>
    /// Maps a specific typed route parameter name to a resource type.
    /// Only maps specific names — excludes generic "id" and "userId" (too ambiguous).
    /// </summary>
    internal static string? MapRouteParamToResourceType(string paramName)
    {
        return paramName switch
        {
            "appointmentId" => SkillswapResources.APPOINTMENT,
            "matchId" or "requestId" => SkillswapResources.MATCH,
            "skillId" or "listingId" or "topicId" => SkillswapResources.SKILL,
            "sessionId" => SkillswapResources.VIDEOCALL,
            "notificationId" or "templateId" => SkillswapResources.NOTIFICATION,
            "alertId" => SkillswapResources.SYSTEM,
            // "id" and "userId" are intentionally excluded — too generic to infer resource type
            _ => null
        };
    }

    internal static string? InferResourceTypeFromPath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Scan all segments for known resource segments.
        // If multiple DISTINCT resource types are found, the path is ambiguous
        // (e.g. /users/calendar/{userId}/sync/{appointmentId} has User + Appointment).
        // Ambiguous paths return null → fail closed. Callers must use explicit
        // ResourceAuthorizeAttribute or typed OwnershipRequirement.
        string? firstType = null;

        for (int i = 0; i < segments.Length; i++)
        {
            var mapped = MapSegmentToResourceType(segments[i].ToLowerInvariant());
            if (mapped != null)
            {
                if (firstType == null)
                {
                    firstType = mapped;
                }
                else if (!string.Equals(firstType, mapped, StringComparison.Ordinal))
                {
                    // Multiple distinct resource types → ambiguous → fail closed
                    return null;
                }
            }
        }

        return firstType;
    }

    internal static string? MapSegmentToResourceType(string segment)
    {
        return segment switch
        {
            // User service: /users/..., /api/users/..., /api/admin/...
            "users" or "user" or "auth" => SkillswapResources.USER,
            // Skill service: /skills/..., /listings/...
            "skills" or "skill" => SkillswapResources.SKILL,
            "listings" or "listing" => SkillswapResources.SKILL,
            // Matchmaking service: /matches/..., /match-requests/...
            "matches" or "match" or "match-requests" => SkillswapResources.MATCH,
            // Appointment service: /appointments/..., /reviews/...
            "appointments" or "appointment" => SkillswapResources.APPOINTMENT,
            "reviews" => SkillswapResources.APPOINTMENT,
            // Videocall service: /api/calls/..., /api/videocall/...
            "videocall" or "videocalls" or "calls" => SkillswapResources.VIDEOCALL,
            // Notification service: /notifications/..., /preferences/..., /reminders/..., /templates/...
            "notifications" or "notification" => SkillswapResources.NOTIFICATION,
            "preferences" or "reminders" or "templates" => SkillswapResources.NOTIFICATION,
            // Admin: /api/admin/...
            "admin" or "system" => SkillswapResources.SYSTEM,
            // "Payment" is NOT in SkillswapResources — payment endpoints
            // require explicit ResourceAuthorizeAttribute.
            _ => null
        };
    }

    private static object? GetResourceDataFromContext(AuthorizationHandlerContext context)
    {
        // Route data only provides an ID — not the domain fields (RequesterId, TargetUserId,
        // OrganizerUserId, ParticipantUserId, HostUserId) that conditional permission
        // evaluation requires. Return null so conditional permissions fail-closed.
        // Callers needing conditional authorization should pass the real domain object
        // as context.Resource directly.
        return null;
    }
}

/// <summary>
/// Ownership authorization handler.
/// Supports both MVC (AuthorizationFilterContext) and Minimal API (HttpContext) resources.
/// Uses resource-aware ID resolution: when resource type is known, prefers the matching
/// typed route param (e.g. appointmentId for Appointment) before falling back to generic "id".
/// </summary>
public class OwnershipAuthorizationHandler : AuthorizationHandler<OwnershipRequirement>
{
    private readonly IResourceAuthorizationService _authorizationService;

    /// <summary>
    /// All known route parameter names that represent a resource ID.
    /// Used for generic (non-resource-aware) resolution.
    /// </summary>
    internal static readonly string[] KnownIdRouteParams = new[]
    {
        "id",
        "appointmentId",
        "requestId",
        "matchId",
        "sessionId",
        "userId",
        "skillId",
        "listingId",
        "notificationId",
        "paymentId",
        "alertId",
        "experienceId",
        "educationId",
        "templateId",
        "topicId",
        "threadId"
    };

    /// <summary>
    /// Maps resource type → preferred route parameter names for that resource.
    /// The first match wins. This allows multi-param routes (e.g. /users/{userId}/calendar/{appointmentId})
    /// to resolve the correct ID based on the resolved resource type.
    /// </summary>
    internal static readonly Dictionary<string, string[]> ResourceTypeToIdParams = new(StringComparer.OrdinalIgnoreCase)
    {
        [SkillswapResources.APPOINTMENT] = new[] { "appointmentId", "id" },
        [SkillswapResources.MATCH] = new[] { "matchId", "requestId", "id" },
        [SkillswapResources.SKILL] = new[] { "skillId", "listingId", "topicId", "id" },
        [SkillswapResources.USER] = new[] { "userId", "id" },
        [SkillswapResources.VIDEOCALL] = new[] { "sessionId", "id" },
        [SkillswapResources.NOTIFICATION] = new[] { "notificationId", "templateId", "id" },
        [SkillswapResources.SYSTEM] = new[] { "alertId", "userId", "id" },
    };

    public OwnershipAuthorizationHandler(IResourceAuthorizationService authorizationService)
    {
        _authorizationService = authorizationService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnershipRequirement requirement)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            context.Fail();
            return;
        }

        var resourceType = requirement.ResourceType ?? ResourceAuthorizationHandler.GetResourceTypeFromContext(context);
        var resourceId = GetResourceIdFromContext(context, resourceType);

        if (string.IsNullOrEmpty(resourceType) || string.IsNullOrEmpty(resourceId))
        {
            context.Fail();
            return;
        }

        var isOwner = await _authorizationService.IsResourceOwnerAsync(userId, resourceType, resourceId);

        if (isOwner)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }

    internal static string? GetResourceIdFromContext(AuthorizationHandlerContext context, string? resourceType)
    {
        // MVC path
        if (context.Resource is AuthorizationFilterContext filterContext)
        {
            return ResolveResourceId(filterContext.RouteData.Values, resourceType);
        }

        // Minimal API path
        if (context.Resource is HttpContext httpContext)
        {
            return ResolveResourceId(httpContext.Request.RouteValues, resourceType);
        }

        return null;
    }

    /// <summary>
    /// Resolves a resource ID from route values using resource-aware resolution.
    /// When resourceType is known, uses the preferred param list for that type.
    /// When unknown, falls back to generic resolution with ambiguity fail-closed.
    /// </summary>
    internal static string? ResolveResourceId(IReadOnlyDictionary<string, object?> routeValues, string? resourceType = null)
    {
        // Resource-aware path: use the preferred param list for this resource type
        if (!string.IsNullOrEmpty(resourceType) && ResourceTypeToIdParams.TryGetValue(resourceType, out var preferredParams))
        {
            foreach (var paramName in preferredParams)
            {
                if (routeValues.TryGetValue(paramName, out var value) && value != null)
                {
                    var stringValue = value.ToString();
                    if (!string.IsNullOrEmpty(stringValue))
                    {
                        return stringValue;
                    }
                }
            }
            // No preferred param found — fail-closed
            return null;
        }

        // Generic fallback: scan all known params, fail-closed on ambiguity
        return ResolveResourceIdGeneric(routeValues);
    }

    /// <summary>
    /// Generic resolution: returns a single unambiguous ID, or null if zero or multiple found.
    /// </summary>
    internal static string? ResolveResourceIdGeneric(IReadOnlyDictionary<string, object?> routeValues)
    {
        string? foundId = null;

        foreach (var paramName in KnownIdRouteParams)
        {
            if (routeValues.TryGetValue(paramName, out var value) && value != null)
            {
                var stringValue = value.ToString();
                if (!string.IsNullOrEmpty(stringValue))
                {
                    if (foundId != null)
                    {
                        // Ambiguous: multiple known ID route params present — fail-closed
                        return null;
                    }
                    foundId = stringValue;
                }
            }
        }

        return foundId;
    }
}

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
