using Girder.Abstractions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Girder.Application.Hosting;

/// <summary>
/// Declaring a provider a registration needs, from anywhere that has an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class ProviderRequirementServiceCollectionExtensions
{
    /// <summary>
    /// Declares that a registration needs a service it does not register
    /// itself. Checked at startup, not here: the provider is usually added
    /// after the call that needs it.
    /// </summary>
    /// <typeparam name="TService">What has to be resolvable.</typeparam>
    /// <param name="services">The collection the requirement is recorded in.</param>
    /// <param name="requiredBy">The call that needs it, as the caller writes it.</param>
    /// <param name="remedy">The call or calls that satisfy the requirement.</param>
    public static IServiceCollection RequiresProvider<TService>(
        this IServiceCollection services,
        string requiredBy,
        string remedy)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.GetProviderRequirements()
                .Add(new ProviderRequirement(typeof(TService), requiredBy, remedy));

        // The check has to be installed by whoever declares, or a service that
        // never calls AddSharedInfrastructure keeps the lazy failure.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IStartupFilter, ProviderRequirementValidator>());

        return services;
    }
}

/// <summary>
/// Refuses to start when a declared requirement has no provider behind it.
/// </summary>
/// <remarks>
/// Without this the container resolves lazily, so a missing provider surfaces
/// on the first request that happens to need it — in production, under load,
/// as a 500 with a stack trace about a type the caller never heard of. Failing
/// at startup names the call and the call that fixes it instead.
/// </remarks>
internal sealed class ProviderRequirementValidator(
    ProviderRequirements requirements,
    IServiceProvider services) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        requirements.ThrowIfUnmet(services);
        return next;
    }
}
