using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Noelia.Infrastructure.Communication;

namespace Noelia.Infrastructure.HealthChecks;

public static class HealthCheckExtensions
{
    public static IServiceCollection AddComprehensiveHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddServiceCommunication(configuration);
        // services.AddScoped<IComprehensiveHealthCheckService, ComprehensiveHealthCheckService>();
        
        return services;
    }
}
