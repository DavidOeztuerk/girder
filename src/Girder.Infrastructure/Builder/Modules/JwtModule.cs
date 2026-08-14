using Girder.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Builder.Modules;

public static class JwtModule
{
    /// <summary>
    /// Add JWT authentication with token revocation support.
    /// </summary>
    public static InfrastructureBuilder AddJwtAuthentication(this InfrastructureBuilder builder)
    {
        builder.JwtEnabled = true;
        builder.Services.AddJwtAuthentication(builder.Configuration, builder.Environment);
        return builder;
    }
}
