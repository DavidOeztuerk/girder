using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Builder;

public class InfrastructureMiddlewareBuilder
{
    public IApplicationBuilder App { get; }
    public IHostEnvironment Environment { get; }
    public string ServiceName { get; }

    public InfrastructureMiddlewareBuilder(
        IApplicationBuilder app,
        IHostEnvironment environment,
        string serviceName)
    {
        App = app;
        Environment = environment;
        ServiceName = serviceName;
    }
}
