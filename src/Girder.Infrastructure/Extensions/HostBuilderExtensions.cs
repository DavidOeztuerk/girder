using Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Infrastructure.Extensions;

/// <summary>
/// Extensions for host builder to configure logging
/// </summary>
public static class HostBuilderExtensions
{
    public static IHostBuilder UseSharedSerilog(this IHostBuilder builder, string serviceName)
    {
        return builder.UseSerilog((context, services, configuration) =>
        {
            LoggingConfiguration.ConfigureSerilog(
                context.Configuration,
                context.HostingEnvironment,
                serviceName);
        });
    }
}