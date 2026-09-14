using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Noelia.Abstractions.Security.Checks;

namespace Noelia.Dashboard;

internal static class DashboardRegistration
{
    internal static void Add(IServiceCollection services, NoeliaDashboardOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<INoeliaDashboard, NoeliaDashboardMarker>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISecurityCheck, DashboardAccessSecurityCheck>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupFilter, NoeliaDashboardStartupFilter>());
    }

    internal static void AddStandaloneSecurityReport(IServiceCollection services)
    {
        services.AddSingleton<StandaloneDashboardSecurityReport>();
        services.AddSingleton<ISecurityCheckReport>(
            provider => provider.GetRequiredService<StandaloneDashboardSecurityReport>());
        services.AddSingleton<IHostedService>(
            provider => provider.GetRequiredService<StandaloneDashboardSecurityReport>());
    }
}
