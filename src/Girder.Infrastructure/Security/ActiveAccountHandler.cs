using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security;

/// <summary>
/// Handler for active account requirement
/// </summary>
public class ActiveAccountHandler(
    ILogger<ActiveAccountHandler> logger) 
    : AuthorizationHandler<ActiveAccountRequirement>
{
    private readonly ILogger<ActiveAccountHandler> _logger = logger;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveAccountRequirement requirement)
    {
        var accountStatus = context.User.FindFirst("account_status")?.Value;

        if (string.Equals(accountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }
        else
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Unknown";
            _logger.LogWarning("Active account check failed for user {UserId} with status {Status}",
                userId, accountStatus);
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
