using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder;

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
public sealed class ProviderRequirements
{
    private readonly List<ProviderRequirement> _requirements = [];

    /// <summary>What the configured modules declared.</summary>
    public IReadOnlyList<ProviderRequirement> All => _requirements;

    internal void Add(ProviderRequirement requirement) => _requirements.Add(requirement);

    /// <summary>Requirements <paramref name="services"/> cannot satisfy.</summary>
    public IReadOnlyList<ProviderRequirement> Unmet(IServiceProvider services) =>
        _requirements.Where(r => services.GetService(r.ServiceType) is null).ToArray();
}

/// <summary>
/// Refuses to start when a configured module has no provider behind it.
/// </summary>
/// <remarks>
/// Without this the container resolves lazily, so a missing provider surfaces
/// on the first request that happens to need it — in production, under load,
/// as a 500 with a stack trace about a type the caller never heard of. Failing
/// at startup names the module and the call that fixes it instead.
/// </remarks>
internal sealed class ProviderRequirementValidator(
    ProviderRequirements requirements,
    IServiceProvider services) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        var unmet = requirements.Unmet(services);

        if (unmet.Count > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                unmet.Select(r => $"  • {r.RequiredBy} needs {r.ServiceType.Name} — call {r.Remedy}"));

            throw new InvalidOperationException(
                $"Girder is missing {unmet.Count} provider registration(s):{Environment.NewLine}{detail}"
                + $"{Environment.NewLine}Provider packages: Girder.Redis, Girder.InMemory, "
                + "Girder.Messaging.MassTransit, Girder.Data.EntityFrameworkCore.");
        }

        return next;
    }
}
