using Microsoft.AspNetCore.Http;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Answers the two questions <c>PermissionMiddleware</c> asks about a request.
/// </summary>
/// <remarks>
/// Girder knows no paths of its own. Without a registered policy, endpoints
/// declare their own requirements through <c>[AllowAnonymous]</c> and
/// <c>[RequirePermission]</c>, which is the preferred way; a policy is for
/// applications that would rather keep the map in one place.
/// </remarks>
public interface IEndpointAccessPolicy
{
    /// <summary>Whether the request may proceed without authentication.</summary>
    bool IsPublic(HttpContext context);

    /// <summary>
    /// The permission the request requires, or <c>null</c> when the policy has
    /// nothing to say about it.
    /// </summary>
    string? RequiredPermission(HttpContext context);
}
