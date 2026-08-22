using Microsoft.Extensions.DependencyInjection;

namespace Girder.Abstractions.Hosting;

/// <summary>
/// A service a module needs but does not register itself, because supplying it
/// means choosing a server.
/// </summary>
/// <param name="ServiceType">What has to be resolvable.</param>
/// <param name="RequiredBy">The module that needs it, e.g. <c>AddCaching()</c>.</param>
/// <param name="Remedy">The calls that satisfy it, in the caller's words.</param>
public sealed record ProviderRequirement(Type ServiceType, string RequiredBy, string Remedy);

/// <summary>
/// Collects what the configured modules need, so it can be checked once
/// everything is registered.
/// </summary>
/// <remarks>
/// Lives here rather than beside the builder because the builder is not the
/// only caller: <c>AddCQRS()</c> stands outside the
/// <c>AddSharedInfrastructure</c> chain and still has a cache to ask for.
/// </remarks>
public sealed class ProviderRequirements
{
    private readonly List<ProviderRequirement> _requirements = [];

    /// <summary>What the configured modules declared.</summary>
    public IReadOnlyList<ProviderRequirement> All => _requirements;

    /// <summary>
    /// Records a requirement. Declaring the same one twice — two modules that
    /// both need a cache, or <c>AddCQRS()</c> called once per assembly — is one
    /// statement, so it is reported once.
    /// </summary>
    public void Add(ProviderRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (!_requirements.Contains(requirement))
        {
            _requirements.Add(requirement);
        }
    }

    /// <summary>
    /// The requirements nothing satisfies — one entry per declaration, so the
    /// same missing service appears once per module that asked for it.
    /// </summary>
    /// <remarks>
    /// Asks whether the type is registered rather than resolving it. Resolving
    /// would build an instance nobody uses, and for a scoped registration it
    /// throws outright when called on the root provider — so a correctly wired
    /// service crashed at startup instead of starting.
    /// <para>
    /// A container that cannot answer the question reports nothing missing:
    /// refusing to start on that basis would be worse than the lazy failure
    /// this check exists to replace.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ProviderRequirement> Unmet(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<IServiceProviderIsService>() is not { } probe)
        {
            return [];
        }

        return _requirements.Where(r => !probe.IsService(r.ServiceType)).ToArray();
    }

    /// <summary>
    /// Refuses to carry on when a declared requirement has no provider behind
    /// it, naming every one of them and the call that fixes each.
    /// </summary>
    /// <remarks>
    /// Reporting one at a time turns a five-minute fix into five restarts.
    /// <para>
    /// Counted by service, not by requirement: two modules that both need a
    /// cache are one registration to add, and saying "2" would send the reader
    /// looking for a second thing to do. Both are still named, because the
    /// reader may be removing one of them rather than adding the provider.
    /// </para>
    /// </remarks>
    public void ThrowIfUnmet(IServiceProvider services)
    {
        var unmet = Unmet(services);

        if (unmet.Count == 0)
        {
            return;
        }

        var byService = unmet.GroupBy(r => r.ServiceType).ToArray();

        var detail = string.Join(Environment.NewLine, byService.Select(Describe));

        throw new InvalidOperationException(
            $"Girder is missing {byService.Length} provider registration(s):{Environment.NewLine}{detail}"
            + $"{Environment.NewLine}Provider packages: Girder.Redis, Girder.InMemory, "
            + "Girder.Messaging.MassTransit, Girder.Data.EntityFrameworkCore.");
    }

    /// <summary>One line: who needs this service, and what supplies it.</summary>
    private static string Describe(IGrouping<Type, ProviderRequirement> service)
    {
        var callers = service.Select(r => r.RequiredBy).Distinct().ToArray();
        var remedies = service.Select(r => r.Remedy).Distinct();

        return $"  • {string.Join(", ", callers)} {(callers.Length == 1 ? "needs" : "need")} "
             + $"{service.Key.Name} — call {string.Join(" / ", remedies)}";
    }
}

/// <summary>
/// Reaching the collector from a plain <see cref="IServiceCollection"/>.
/// </summary>
public static class ProviderRequirementCollectionExtensions
{
    /// <summary>
    /// The one collector for this service collection, created on first ask.
    /// </summary>
    /// <remarks>
    /// One instance, not one per caller: registering a second would shadow the
    /// first, and whichever module declared its requirements earlier would have
    /// them silently dropped.
    /// </remarks>
    public static ProviderRequirements GetProviderRequirements(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var descriptor in services)
        {
            if (descriptor.ServiceType == typeof(ProviderRequirements)
                && descriptor.ImplementationInstance is ProviderRequirements existing)
            {
                return existing;
            }
        }

        var requirements = new ProviderRequirements();
        services.AddSingleton(requirements);
        return requirements;
    }
}
