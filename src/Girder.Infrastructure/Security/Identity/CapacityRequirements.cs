using Girder.Core.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Girder.Infrastructure.Security.Identity;

/// <summary>Requires the caller to be acting for a company.</summary>
public sealed class ActingForCompanyRequirement : IAuthorizationRequirement;

/// <summary>Requires the caller to be acting for themselves.</summary>
public sealed class ActingAsSelfRequirement : IAuthorizationRequirement;

/// <summary>
/// Succeeds when the current principal acts for a company.
/// </summary>
public sealed class ActingForCompanyHandler : AuthorizationHandler<ActingForCompanyRequirement>
{
    private readonly ICurrentPrincipal _principal;

    public ActingForCompanyHandler(ICurrentPrincipal principal) => _principal = principal;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActingForCompanyRequirement requirement)
    {
        if (_principal.Current?.Acting is Capacity.ForCompany)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Succeeds when the current principal acts for themselves.
/// </summary>
public sealed class ActingAsSelfHandler : AuthorizationHandler<ActingAsSelfRequirement>
{
    private readonly ICurrentPrincipal _principal;

    public ActingAsSelfHandler(ICurrentPrincipal principal) => _principal = principal;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActingAsSelfRequirement requirement)
    {
        if (_principal.Current?.Acting is Capacity.AsSelf)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
