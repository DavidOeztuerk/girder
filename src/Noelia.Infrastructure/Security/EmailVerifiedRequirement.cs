using Microsoft.AspNetCore.Authorization;

namespace Noelia.Infrastructure.Security;

/// <summary>
/// Custom authorization requirement for email verification
/// </summary>
public class EmailVerifiedRequirement : IAuthorizationRequirement
{
}
