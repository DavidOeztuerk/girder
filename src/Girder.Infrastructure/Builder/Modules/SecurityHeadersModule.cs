namespace Infrastructure.Builder.Modules;

public static class SecurityHeadersModule
{
    /// <summary>
    /// Add security headers middleware support.
    /// The SecurityHeadersMiddleware is registered via UseMiddleware in the pipeline.
    /// </summary>
    public static InfrastructureBuilder AddSecurityHeaders(this InfrastructureBuilder builder)
    {
        builder.SecurityHeadersEnabled = true;
        // SecurityHeadersMiddleware is used directly via UseMiddleware<> in the pipeline,
        // no service registration needed here.
        return builder;
    }
}
