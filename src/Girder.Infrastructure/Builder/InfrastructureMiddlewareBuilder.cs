using Girder.Abstractions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Builder;

/// <summary>
/// Assembles the request pipeline, and skips the steps this service did not ask
/// for.
/// </summary>
/// <remarks>
/// <para><strong>The composition decides on both sides.</strong>
/// <c>AddGirder</c> records what was set up and what was deliberately left out;
/// this reads that record, so <c>Without(module, reason)</c> takes effect once
/// rather than twice. Before, the service side could leave a module out and the
/// pipeline would still try to use it — which either threw at startup
/// (<c>UseRateLimiting()</c> without a store) or forced every caller to copy
/// Girder's chain into their own composition root, minus the lines they wanted
/// gone. A copy like that goes quietly wrong the first time Girder adds a
/// step.</para>
/// <para>With no <see cref="GirderComposition"/> in the container — the older
/// <c>AddSharedInfrastructure</c> path, or a hand-built pipeline — nothing is
/// skipped. An absent record is not a record saying no.</para>
/// </remarks>
public class InfrastructureMiddlewareBuilder
{
    private readonly GirderComposition? _composition;

    /// <summary>The pipeline being assembled.</summary>
    public IApplicationBuilder App { get; }

    /// <summary>Where it runs.</summary>
    public IHostEnvironment Environment { get; }

    /// <summary>What the service calls itself.</summary>
    public string ServiceName { get; }

    /// <summary>Reads the composition the container carries, if there is one.</summary>
    /// <param name="app">The pipeline being assembled.</param>
    /// <param name="environment">Where it runs.</param>
    /// <param name="serviceName">What the service calls itself.</param>
    public InfrastructureMiddlewareBuilder(
        IApplicationBuilder app,
        IHostEnvironment environment,
        string serviceName)
    {
        App = app;
        Environment = environment;
        ServiceName = serviceName;
        _composition = app.ApplicationServices.GetService<GirderComposition>();
    }

    /// <summary>
    /// Whether a pipeline step belonging to <paramref name="module"/> should be
    /// added.
    /// </summary>
    /// <remarks>
    /// True when the module was set up, and true as well when nothing recorded a
    /// composition at all: silence there means "not asked", not "refused".
    /// </remarks>
    /// <param name="module">The module the step belongs to.</param>
    public bool Runs(GirderModule module) =>
        _composition is null || _composition.Included.Contains(module);

    /// <summary>
    /// Adds one pipeline step, unless its module was left out.
    /// </summary>
    /// <remarks>
    /// The gate sits <em>before</em> <see cref="Requires{TService}"/> on purpose:
    /// a module that was left out registered nothing, so demanding its service
    /// would turn a recorded decision into a startup failure.
    /// </remarks>
    /// <param name="module">The module the step belongs to.</param>
    /// <param name="add">The step, as it would be written unconditionally.</param>
    public InfrastructureMiddlewareBuilder Step(
        GirderModule module,
        Action<InfrastructureMiddlewareBuilder> add)
    {
        ArgumentNullException.ThrowIfNull(add);

        if (Runs(module))
        {
            add(this);
        }

        return this;
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
