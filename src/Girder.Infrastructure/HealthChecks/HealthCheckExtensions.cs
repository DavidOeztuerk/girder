using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Infrastructure.Communication;

namespace Infrastructure.HealthChecks;

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
