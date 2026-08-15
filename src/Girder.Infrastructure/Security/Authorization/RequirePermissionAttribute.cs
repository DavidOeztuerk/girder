using Microsoft.AspNetCore.Authorization;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Declares the permission an endpoint requires.
/// </summary>
/// <remarks>
/// Two mechanisms read this, and both have to see the same attribute: the
/// framework's authorization pipeline resolves <see cref="AuthorizeAttribute.Policy"/>
/// through <c>PermissionPolicyProvider</c>, and <c>PermissionMiddleware</c>
/// reads <see cref="Permission"/> straight off the endpoint metadata. There
/// used to be one attribute of this name per mechanism, in two namespaces —
/// which of the two a call site got depended on its <c>using</c> directives,
/// and picking the one whose mechanism was not wired left the endpoint open.
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
