using Microsoft.AspNetCore.Authorization;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Declares the permission an endpoint requires, e.g.
/// <c>[RequirePermission("users:read")]</c>.
/// </summary>
/// <remarks>
/// Enforced by two mechanisms, either of which is sufficient: the framework's
/// authorization pipeline resolves <see cref="AuthorizeAttribute.Policy"/> via
/// <c>PermissionPolicyProvider</c> (registered by <c>AddPermissionAuthorization</c>),
/// and <c>PermissionMiddleware</c> reads <see cref="Permission"/> from the
/// endpoint metadata. Register at least one of them, or the attribute is inert.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    /// <summary>Policy name prefix that <c>PermissionPolicyProvider</c> answers.</summary>
    public const string PolicyPrefix = "Permission:";

    public RequirePermissionAttribute(string permission, string? resource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        Permission = permission;
        Resource = resource;
        Policy = PolicyPrefix + permission;
    }

    /// <summary>The permission itself, e.g. <c>users:read</c>.</summary>
    public string Permission { get; }

    /// <summary>
    /// Narrows the permission to one resource, checked as
    /// <c>{permission}:{resource}</c>.
    /// </summary>
    public string? Resource { get; }
}
