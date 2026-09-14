using Microsoft.Extensions.DependencyInjection;

namespace Noelia.Infrastructure.Builder.Modules;

public static class HealthChecksModule
{
    /// <summary>
    /// Add health check registrations (Redis, RabbitMQ, etc. are added by their respective modules).
    /// </summary>
    public static InfrastructureBuilder AddHealthChecks(this InfrastructureBuilder builder)
    {
        builder.HealthChecksEnabled = true;
        builder.Services.AddHealthChecks();
        return builder;
    }
}
