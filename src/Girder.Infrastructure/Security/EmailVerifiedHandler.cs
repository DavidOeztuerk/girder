using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security;

/// <summary>
/// Handler for email verification requirement
/// </summary>
public class EmailVerifiedHandler(
    ILogger<EmailVerifiedHandler> logger) 
    : AuthorizationHandler<EmailVerifiedRequirement>
{
    private readonly ILogger<EmailVerifiedHandler> _logger = logger;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EmailVerifiedRequirement requirement)
    {
        var emailVerifiedClaim = context.User.FindFirst("email_verified")?.Value;

        if (bool.TryParse(emailVerifiedClaim, out var isEmailVerified) && isEmailVerified)
        {
            context.Succeed(requirement);
        }
        else
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";
            _logger.LogWarning("Email verification check failed for user {UserId}", userId);
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
