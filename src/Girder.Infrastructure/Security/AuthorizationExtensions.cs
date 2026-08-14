using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Security;

/// <summary>
/// Extension methods for configuring authorization
/// </summary>
public static class AuthorizationExtensions
{
    public static IServiceCollection AddSkillSwapAuthorization(this IServiceCollection services)
    {
        // Use the standard AddAuthorization method instead of AddAuthorizationBuilder
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.RequireAdminRole, policy =>
                policy.RequireRole(Roles.Admin, Roles.SuperAdmin));

            options.AddPolicy(Policies.RequireModeratorRole, policy =>
                policy.RequireRole(Roles.Moderator, Roles.Admin, Roles.SuperAdmin));

            options.AddPolicy(Policies.RequireUserRole, policy =>
                policy.RequireRole(Roles.User, Roles.Moderator, Roles.Admin, Roles.SuperAdmin));

            options.AddPolicy(Policies.RequireVerifiedEmail, policy =>
                policy.AddRequirements(new EmailVerifiedRequirement()));

            options.AddPolicy(Policies.RequireActiveAccount, policy =>
                policy.AddRequirements(new ActiveAccountRequirement()));

            options.AddPolicy(Policies.CanManageUsers, policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(Roles.Admin) ||
                    context.User.IsInRole(Roles.SuperAdmin) ||
                    context.User.HasClaim("permission", Permissions.UsersManageRoles)));

            options.AddPolicy(Policies.CanManageSkills, policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(Roles.Admin) ||
                    context.User.IsInRole(Roles.SuperAdmin) ||
                    context.User.HasClaim("permission", Permissions.SkillsManageCategories)));

            options.AddPolicy(Policies.CanViewSystemLogs, policy =>
                policy.RequireAssertion(context =>
                    context.User.IsInRole(Roles.Admin) ||
                    context.User.IsInRole(Roles.SuperAdmin) ||
                    context.User.HasClaim("permission", Permissions.SystemViewLogs)));

            var permissionFields = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var field in permissionFields)
            {
                var permission = field.GetValue(null)?.ToString();
                if (!string.IsNullOrWhiteSpace(permission))
                {
                    options.AddPolicy(permission, policy =>
                        policy.RequireClaim("permission", permission));
                }
            }
        });

        // // Register authorization handlers
        services.AddScoped<IAuthorizationHandler, ResourceOwnerHandler>();
        services.AddScoped<IAuthorizationHandler, EmailVerifiedHandler>();
        services.AddScoped<IAuthorizationHandler, ActiveAccountHandler>();

        return services;
    }
}