using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using System.Text.Json;

namespace Infrastructure.Security.Authorization;

/// <summary>
/// Middleware for automatic resource authorization
/// </summary>
public class ResourceAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IResourceAuthorizationService _authorizationService;
    private readonly ILogger<ResourceAuthorizationMiddleware> _logger;

    public ResourceAuthorizationMiddleware(
        RequestDelegate next,
        IResourceAuthorizationService authorizationService,
        ILogger<ResourceAuthorizationMiddleware> logger)
    {
        _next = next;
        _authorizationService = authorizationService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip for non-authenticated requests
        if (!context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        // Skip for endpoints that don't require resource authorization
        if (ShouldSkipAuthorization(context))
        {
            await _next(context);
            return;
        }

        try
        {
            var authResult = await CheckResourceAuthorizationAsync(context);
            
            if (!authResult.Succeeded)
            {
                _logger.LogWarning(
                    "Resource authorization failed for user {UserId} on {Method} {Path}: {Reasons}",
                    context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                    context.Request.Method,
                    context.Request.Path,
                    string.Join(", ", authResult.FailureReasons));

                await HandleAuthorizationFailure(context, authResult);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during resource authorization check");
            // Continue processing - don't block valid requests due to authorization check errors
        }

        await _next(context);
    }

    private async Task<AuthorizationResult> CheckResourceAuthorizationAsync(HttpContext context)
    {
        var resourceInfo = ExtractResourceInformation(context);
        
        if (resourceInfo == null)
        {
            return AuthorizationResult.Success(); // No resource information available
        }

        var action = MapHttpMethodToAction(context.Request.Method);
        
        return await _authorizationService.AuthorizeAsync(
            context.User,
            resourceInfo.ResourceType,
            action,
            resourceInfo.ResourceData);
    }

    private static ResourceInformation? ExtractResourceInformation(HttpContext context)
    {
        var routeData = context.Request.RouteValues;
        
        // Extract controller name as resource type
        if (!routeData.TryGetValue("controller", out var controllerObj))
        {
            return null;
        }

        var controller = controllerObj?.ToString();
        if (string.IsNullOrEmpty(controller))
        {
            return null;
        }

        // Map controller names to resource types
        var resourceType = MapControllerToResourceType(controller);
        
        // Extract resource ID if available
        var resourceData = new Dictionary<string, object?>();
        
        if (routeData.TryGetValue("id", out var id))
        {
            resourceData["Id"] = id;
        }

        // Extract additional route parameters
        foreach (var kvp in routeData.Where(kvp => kvp.Key != "controller" && kvp.Key != "action"))
        {
            resourceData[kvp.Key] = kvp.Value;
        }

        return new ResourceInformation
        {
            ResourceType = resourceType,
            ResourceData = resourceData.Any() ? resourceData : null
        };
    }

    private static string MapControllerToResourceType(string controller)
    {
        return controller.ToLowerInvariant() switch
        {
            "users" or "user" => SkillswapResources.USER,
            "skills" or "skill" => SkillswapResources.SKILL,
            "matches" or "match" => SkillswapResources.MATCH,
            "appointments" or "appointment" => SkillswapResources.APPOINTMENT,
            "videocalls" or "videocall" => SkillswapResources.VIDEOCALL,
            "notifications" or "notification" => SkillswapResources.NOTIFICATION,
            "admin" or "system" => SkillswapResources.SYSTEM,
            _ => controller
        };
    }

    private static string MapHttpMethodToAction(string httpMethod)
    {
        return httpMethod.ToUpperInvariant() switch
        {
            "GET" => SkillswapActions.READ,
            "POST" => SkillswapActions.CREATE,
            "PUT" or "PATCH" => SkillswapActions.UPDATE,
            "DELETE" => SkillswapActions.DELETE,
            _ => SkillswapActions.READ
        };
    }

    private static bool ShouldSkipAuthorization(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        
        // Skip for authentication endpoints
        if (path?.Contains("/auth/") == true ||
            path?.Contains("/login") == true ||
            path?.Contains("/register") == true ||
            path?.Contains("/logout") == true)
        {
            return true;
        }

        // Skip for health checks and metrics
        if (path?.Contains("/health") == true ||
            path?.Contains("/metrics") == true ||
            path?.Contains("/swagger") == true)
        {
            return true;
        }

        // Skip for public endpoints
        if (path?.Contains("/public/") == true)
        {
            return true;
        }

        return false;
    }

    private async Task HandleAuthorizationFailure(HttpContext context, AuthorizationResult authResult)
    {
        context.Response.StatusCode = 403; // Forbidden
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            error = "authorization_failed",
            message = "You do not have permission to access this resource",
            details = authResult.FailureReasons,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private class ResourceInformation
    {
        public string ResourceType { get; set; } = string.Empty;
        public object? ResourceData { get; set; }
    }
}

/// <summary>
/// Extension methods for using resource authorization middleware
/// </summary>
public static class ResourceAuthorizationMiddlewareExtensions
{
    /// <summary>
    /// Use resource authorization middleware
    /// </summary>
    public static IApplicationBuilder UseResourceAuthorization(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ResourceAuthorizationMiddleware>();
    }
}

/// <summary>
/// Attributes for resource authorization
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ResourceAuthorizeAttribute : Attribute
{
    public string? Resource { get; set; }
    public string? Action { get; set; }
    public bool RequireOwnership { get; set; }

    public ResourceAuthorizeAttribute(string? resource = null, string? action = null)
    {
        Resource = resource;
        Action = action;
    }
}

/// <summary>
/// Attribute to skip resource authorization
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SkipResourceAuthorizationAttribute : Attribute
{
}

/// <summary>
/// Attribute to require resource ownership
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireOwnershipAttribute : Attribute
{
    public string? ResourceType { get; set; }

    public RequireOwnershipAttribute(string? resourceType = null)
    {
        ResourceType = resourceType;
    }
}