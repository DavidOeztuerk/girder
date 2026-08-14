using Girder.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Security.Identity;

public static class IdentityExtensions
{
    /// <summary>
    /// Registers principal resolution and the capacity policies.
    /// </summary>
    public static IServiceCollection AddGirderIdentity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IPrincipalFactory, PrincipalFactory>();
        services.AddScoped<ICurrentPrincipal, HttpContextCurrentPrincipal>();
        // Scoped, not singleton: both handlers depend on the scoped ICurrentPrincipal.
        services.AddScoped<IAuthorizationHandler, ActingForCompanyHandler>();
        services.AddScoped<IAuthorizationHandler, ActingAsSelfHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(GirderPolicies.ActingForCompany, policy =>
                policy.RequireAuthenticatedUser()
                      .AddRequirements(new ActingForCompanyRequirement()))
            .AddPolicy(GirderPolicies.ActingAsSelf, policy =>
                policy.RequireAuthenticatedUser()
                      .AddRequirements(new ActingAsSelfRequirement()));

        return services;
    }

    /// <summary>
    /// Resolves the principal for each request.
    /// </summary>
    /// <remarks>
    /// Place after <c>UseAuthentication</c> and before <c>UseAuthorization</c>.
    /// </remarks>
    public static IApplicationBuilder UseGirderPrincipal(this IApplicationBuilder app) =>
        app.UseMiddleware<PrincipalMiddleware>();
}
