using Girder.Infrastructure.Authorization;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Authorization;

namespace Girder.Infrastructure.Builder.Modules;

public static class AuthorizationModule
{
    /// <summary>
    /// Add role-based authorization (policies, handlers).
    /// </summary>
    /// <remarks>
    /// Includes the permission policy provider, because <c>[RequirePermission]</c>
    /// names a <c>Permission:</c> policy that nothing else answers. Without it
    /// the framework rejects the request as "policy not found" — fail-closed,
    /// but on every call to an endpoint the attribute was meant to protect.
    /// Registered last so it wins over the default provider it wraps.
    /// </remarks>
    public static InfrastructureBuilder AddAuthorization(this InfrastructureBuilder builder)
    {
        builder.AuthorizationEnabled = true;
        builder.Services.AddGirderAuthorization();
        builder.Services.AddPermissionAuthorization();
        return builder;
    }

    /// <summary>
    /// Add resource-based authorization (permission resolver, resource/ownership handlers, policies).
    /// Opt-in — requires explicit call. Supports both MVC and Minimal API endpoints.
    /// </summary>
    public static InfrastructureBuilder AddResourceAuthorization(this InfrastructureBuilder builder)
    {
        builder.AuthorizationEnabled = true;
        builder.Services.AddResourceAuthorization(builder.Configuration);
        return builder;
    }
}
