using Microsoft.AspNetCore.Authorization;

namespace Noelia.Infrastructure.Security;

/// <summary>
/// Custom authorization requirement for active account
/// </summary>
public class ActiveAccountRequirement : IAuthorizationRequirement
{
}
