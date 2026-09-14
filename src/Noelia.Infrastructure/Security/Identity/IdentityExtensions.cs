using Noelia.Core.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Noelia.Infrastructure.Security.Identity;

public static class IdentityExtensions
{
    /// <summary>
    /// Registers principal resolution and the capacity policies.
    /// </summary>
    public static IServiceCollection AddNoeliaIdentity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IPrincipalFactory, PrincipalFactory>();
        services.AddScoped<ICurrentPrincipal, HttpContextCurrentPrincipal>();
        // Scoped, not singleton: both handlers depend on the scoped ICurrentPrincipal.
        services.AddScoped<IAuthorizationHandler, ActingForCompanyHandler>();
        services.AddScoped<IAuthorizationHandler, ActingAsSelfHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(NoeliaPolicies.ActingForCompany, policy =>
                policy.RequireAuthenticatedUser()
                      .AddRequirements(new ActingForCompanyRequirement()))
            .AddPolicy(NoeliaPolicies.ActingAsSelf, policy =>
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
    public static IApplicationBuilder UseNoeliaPrincipal(this IApplicationBuilder app) =>
        app.UseMiddleware<PrincipalMiddleware>();
}
