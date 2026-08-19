using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// Asserts that a pipeline step's service is registered, and names the call
    /// that registers it when it is not.
    /// </summary>
    /// <remarks>
    /// Middleware is built on the first request that reaches it, so a missing
    /// registration otherwise surfaces in production as a 500 naming a type the
    /// reader never wrote. Composition is the moment both halves are known.
    /// <para>
    /// Asks the container whether the type is registered rather than resolving
    /// it: a scoped service cannot be resolved from the root provider, and
    /// building one here would create an instance the application never uses.
    /// </para>
    /// </remarks>
    /// <typeparam name="TService">What the step's middleware takes.</typeparam>
    /// <param name="step">The pipeline call, as the caller writes it.</param>
    /// <param name="remedy">The call or calls that register the service.</param>
    /// <exception cref="InvalidOperationException">The service is not registered.</exception>
    public InfrastructureMiddlewareBuilder Requires<TService>(string step, string remedy)
        where TService : class
    {
        var probe = App.ApplicationServices.GetService<IServiceProviderIsService>();

        // Without the feature the container cannot answer, and refusing to
        // compose on that basis would be worse than the lazy failure.
        if (probe is null || probe.IsService(typeof(TService)))
        {
            return this;
        }

        throw new InvalidOperationException(
            $"{step} needs {typeof(TService).Name}, which nothing registered. "
            + $"Call {remedy} while configuring services.");
    }
}
