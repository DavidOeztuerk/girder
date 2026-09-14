namespace Noelia.Abstractions.Hosting;

/// <summary>One package and call that can satisfy a module requirement.</summary>
/// <param name="PackageId">The exact package to reference.</param>
/// <param name="Registration">The exact registration call to make.</param>
public sealed record NoeliaProviderHint(string PackageId, string Registration)
{
    /// <summary>A readable instruction for startup diagnostics.</summary>
    public override string ToString() => $"{PackageId} → {Registration}";
}

/// <summary>A service an active module needs from another module or provider.</summary>
/// <param name="ServiceType">The required service contract.</param>
/// <param name="Providers">Known packages and calls that provide it.</param>
public sealed record NoeliaServiceRequirement(
    Type ServiceType,
    IReadOnlyList<NoeliaProviderHint> Providers);

/// <summary>A service an active module promises to register.</summary>
/// <param name="ServiceType">The provided service contract.</param>
/// <param name="PackageId">The package making the promise.</param>
/// <param name="Registration">The public call that activates it.</param>
public sealed record NoeliaServiceProvision(
    Type ServiceType,
    string PackageId,
    string Registration);

/// <summary>What one module requires from others and provides to them.</summary>
public sealed record NoeliaModuleContract(
    NoeliaModule Module,
    IReadOnlyList<NoeliaServiceRequirement> Requirements,
    IReadOnlyList<NoeliaServiceProvision> Provisions);

/// <summary>Builds a module contract beside the registration it describes.</summary>
public sealed class NoeliaModuleContractBuilder
{
    private readonly NoeliaModule _module;
    private readonly List<NoeliaServiceRequirement> _requirements = [];
    private readonly List<NoeliaServiceProvision> _provisions = [];

    /// <summary>Starts the contract for <paramref name="module"/>.</summary>
    public NoeliaModuleContractBuilder(NoeliaModule module) => _module = module;

    /// <summary>Declares a service the module cannot operate without.</summary>
    public NoeliaModuleContractBuilder Requires<TService>(params NoeliaProviderHint[] providers)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(providers);

        if (providers.Length == 0)
        {
            throw new ArgumentException(
                $"Requirement {typeof(TService).Name} needs at least one package and registration hint.",
                nameof(providers));
        }

        _requirements.Add(new NoeliaServiceRequirement(typeof(TService), providers));
        return this;
    }

    /// <summary>Declares a service the module registers when it is active.</summary>
    public NoeliaModuleContractBuilder Provides<TService>(string packageId, string registration)
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registration);

        _provisions.Add(new NoeliaServiceProvision(typeof(TService), packageId, registration));
        return this;
    }

    /// <summary>Produces the immutable contract.</summary>
    public NoeliaModuleContract Build() => new(_module, _requirements.ToArray(), _provisions.ToArray());
}

/// <summary>A module's executable registration and its public contract.</summary>
public sealed record NoeliaModuleRegistration(
    NoeliaModule Module,
    Action<NoeliaBuilder> Register,
    NoeliaModuleContract Contract);
