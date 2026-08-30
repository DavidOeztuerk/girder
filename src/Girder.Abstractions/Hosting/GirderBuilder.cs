using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Abstractions.Hosting;

/// <summary>
/// Chooses what Girder sets up for this service.
/// </summary>
/// <remarks>
/// Nothing is set up unless it is asked for. <see cref="UseDefaults"/> is the
/// one call that asks for the usual set, and it is a call rather than an
/// assumption so that a composition root says what it runs. Removing it leaves a
/// service with no infrastructure at all — the same shape as Entity Framework
/// without a provider, and for the same reason: the choice belongs to whoever
/// has to operate the result.
/// <para>
/// <see cref="Use(GirderModule)"/> adds and
/// <see cref="Without(GirderModule, string)"/> removes, in any order, and the
/// last mention of a module wins. The order modules are registered in is
/// Girder's, not the order they were named: modules depend on one another, and a
/// caller who reorders two lines should not change what the service does.
/// </para>
/// </remarks>
public sealed class GirderBuilder
{
    private readonly Dictionary<GirderModule, Action<GirderBuilder>> _catalogue;
    private readonly List<GirderModule> _order;
    private readonly Dictionary<GirderModule, string> _excluded = [];
    private readonly HashSet<GirderModule> _included = [];
    private readonly IReadOnlyList<GirderModule> _defaults;

    /// <summary>
    /// Built by <c>AddGirder</c>, which supplies the modules it knows how to set
    /// up and which of them the default set asks for.
    /// </summary>
    public GirderBuilder(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName,
        IReadOnlyList<KeyValuePair<GirderModule, Action<GirderBuilder>>> catalogue,
        IReadOnlyList<GirderModule> defaults)
    {
        Services = services;
        Configuration = configuration;
        Environment = environment;
        ServiceName = serviceName;

        _catalogue = catalogue.ToDictionary(entry => entry.Key, entry => entry.Value);
        _order = catalogue.Select(entry => entry.Key).ToList();
        _defaults = defaults;
    }

    /// <summary>The container the modules register into.</summary>
    public IServiceCollection Services { get; }

    /// <summary>The service's configuration.</summary>
    public IConfiguration Configuration { get; }

    /// <summary>Which environment this is, for the settings that differ.</summary>
    public IHostEnvironment Environment { get; }

    /// <summary>The service's own name, as it appears in logs and traces.</summary>
    public string ServiceName { get; }

    /// <summary>
    /// The set a service gets when it has no reason to differ.
    /// </summary>
    /// <remarks>
    /// Everything here is safe to run without further configuration and useful
    /// on nearly every service. What needs a provider — password hashing, token
    /// sessions — is not included: it would fail at startup for a service that
    /// never asked for it. Add those with <see cref="Use(GirderModule)"/>.
    /// <para>
    /// Not calling this is allowed and means exactly what it says. There is no
    /// implicit default anywhere else.
    /// </para>
    /// </remarks>
    public GirderBuilder UseDefaults()
    {
        foreach (var module in _defaults)
        {
            Use(module);
        }

        return this;
    }

    /// <summary>
    /// Sets up a module Girder knows.
    /// </summary>
    /// <remarks>
    /// Undoes a previous <see cref="Without(GirderModule, string)"/> for the
    /// same module: the last mention wins.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The module is not one Girder knows. A package's own module has to arrive
    /// with its registration — see <see cref="Use(GirderModule, Action{GirderBuilder})"/>.
    /// </exception>
    public GirderBuilder Use(GirderModule module)
    {
        if (!_catalogue.ContainsKey(module))
        {
            throw new ArgumentException(
                $"Girder has no module called '{module}'. A module from another package "
                + $"has to be added with Use({module}, register) so its registration comes with it.",
                nameof(module));
        }

        _excluded.Remove(module);
        _included.Add(module);
        return this;
    }

    /// <summary>
    /// Sets up a module from outside Girder.
    /// </summary>
    /// <remarks>
    /// The extension point. A provider package writes an extension method on
    /// this builder and hands over its own module name and the registration to
    /// run; Girder needs to know nothing about it, and the module then behaves
    /// like any other — it appears in the composition, and it can be left out
    /// with a reason.
    /// <para>
    /// Naming the same module twice replaces the earlier registration, so a
    /// second call is a correction rather than a duplicate.
    /// </para>
    /// </remarks>
    /// <param name="module">The module's name, prefixed with its package.</param>
    /// <param name="register">What to register. Runs once, in catalogue order.</param>
    public GirderBuilder Use(GirderModule module, Action<GirderBuilder> register)
    {
        ArgumentNullException.ThrowIfNull(register);

        if (!_catalogue.ContainsKey(module))
        {
            _order.Add(module);
        }

        _catalogue[module] = register;
        _excluded.Remove(module);
        _included.Add(module);
        return this;
    }

    /// <summary>
    /// Leaves a module out, on the record.
    /// </summary>
    /// <remarks>
    /// The reason is required, and it is required because this is the one place
    /// where it will still be readable in a year. A decision recorded in a
    /// document nobody opens is a decision nobody can review; here it sits next
    /// to what it explains.
    /// <para>
    /// Naming a module Girder does not know is allowed: a package may be removed
    /// before the line that excluded it is, and failing then would turn a
    /// tidy-up into an outage.
    /// </para>
    /// </remarks>
    /// <param name="module">The module to leave out.</param>
    /// <param name="reason">Why. Recorded in <see cref="GirderComposition"/>.</param>
    /// <exception cref="ArgumentException">The reason is empty.</exception>
    public GirderBuilder Without(GirderModule module, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                $"Leaving out '{module}' needs a reason — it is what makes the "
                + "decision reviewable later.",
                nameof(reason));
        }

        _included.Remove(module);
        _excluded[module] = reason;
        return this;
    }

    /// <summary>Runs the chosen registrations and reports what was chosen.</summary>
    public GirderComposition Build()
    {
        var included = _order.Where(_included.Contains).ToArray();

        foreach (var module in included)
        {
            _catalogue[module](this);
        }

        var composition = new GirderComposition(included, _excluded);
        Services.AddSingleton(composition);
        return composition;
    }
}
