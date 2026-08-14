using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Girder.Infrastructure.Security;

/// <summary>
/// Handler for resource ownership authorization
/// </summary>
public class ResourceOwnerHandler(
    IHttpContextAccessor httpContextAccessor,
    ILogger<ResourceOwnerHandler> logger)
    : AuthorizationHandler<ResourceOwnerRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;
    private readonly ILogger<ResourceOwnerHandler> _logger = logger;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourceOwnerRequirement requirement)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("Resource owner check failed: No user ID found in claims");
            context.Fail();
            return Task.CompletedTask;
        }

        // Check if user is admin (admins can access all resources)
        if (context.User.IsInRole(Roles.Admin) || context.User.IsInRole(Roles.SuperAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Get resource ID from route parameters
        var resourceId = httpContext.Request.RouteValues[requirement.ResourceIdParameter]?.ToString();

        if (string.IsNullOrEmpty(resourceId))
        {
            _logger.LogWarning("Resource owner check failed: No resource ID found in route");
            context.Fail();
            return Task.CompletedTask;
        }

        // Check if the user owns the resource
        // Note: In a real implementation, you might need to query the database
        // For now, we'll use a simple check if the resource ID matches user ID
        if (resourceId.Equals(userId, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("Resource owner check failed: User {UserId} attempted to access resource {ResourceId}",
                userId, resourceId);
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
