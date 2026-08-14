using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Authorization;

namespace Girder.Infrastructure.Builder.Modules;

public static class AuthorizationModule
{
    /// <summary>
    /// Add role-based authorization (policies, handlers).
    /// </summary>
    public static InfrastructureBuilder AddAuthorization(this InfrastructureBuilder builder)
    {
        builder.AuthorizationEnabled = true;
        builder.Services.AddGirderAuthorization();
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
