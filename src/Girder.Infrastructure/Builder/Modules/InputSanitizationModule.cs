using Infrastructure.Security.InputSanitization;

namespace Infrastructure.Builder.Modules;

public static class InputSanitizationModule
{
    /// <summary>
    /// Add input sanitization, validation, and injection detection.
    /// </summary>
    public static InfrastructureBuilder AddInputSanitization(this InfrastructureBuilder builder)
    {
        builder.InputSanitizationEnabled = true;
        builder.Services.AddInputSanitization(builder.Configuration);
        return builder;
    }
}
