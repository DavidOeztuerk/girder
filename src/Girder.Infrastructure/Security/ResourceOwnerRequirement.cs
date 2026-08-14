using Microsoft.AspNetCore.Authorization;

namespace Infrastructure.Security;

/// <summary>
/// Custom authorization requirement for resource ownership
/// </summary>
public class ResourceOwnerRequirement(
    string resourceIdParameter = "id")
    : IAuthorizationRequirement
{
    public string ResourceIdParameter { get; } = resourceIdParameter;
}
