using Girder.Infrastructure.Security.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

public static class SecurityHeadersModule
{
    /// <summary>
    /// Adds the security header service and its options, bound from the
    /// <c>SecurityHeaders</c> and <c>SecurityHeadersMiddleware</c> configuration
    /// sections. Add <c>UseSecurityHeaders()</c> to the pipeline to apply them.
    /// </summary>
    public static InfrastructureBuilder AddSecurityHeaders(this InfrastructureBuilder builder)
    {
        builder.SecurityHeadersEnabled = true;

        builder.Services.AddSecurityHeaders(builder.Configuration);

        return builder;
    }
}
