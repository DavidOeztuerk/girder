using Girder.Infrastructure.Resilience;

namespace Girder.Infrastructure.Builder.Modules;

public static class ResilienceModule
{
    /// <summary>
    /// Add resilience services (Circuit Breaker, Retry Policy).
    /// </summary>
    public static InfrastructureBuilder AddResilience(this InfrastructureBuilder builder)
    {
        builder.ResilienceEnabled = true;
        builder.Services.AddResilience();
        return builder;
    }
}
