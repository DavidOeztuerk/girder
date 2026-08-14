namespace Contracts.Common;

/// <summary>
/// Interface for versioned API contracts
/// </summary>
public interface IVersionedContract
{
    /// <summary>
    /// The API version this contract supports
    /// </summary>
    string ApiVersion { get; }
}

/// <summary>
/// Interface for contracts that can be migrated between versions
/// </summary>
/// <typeparam name="TPrevious">Previous version contract type</typeparam>
public interface IMigratableContract<in TPrevious> : IVersionedContract
    where TPrevious : IVersionedContract
{
    /// <summary>
    /// Creates this contract version from a previous version
    /// </summary>
    /// <param name="previous">Previous version contract</param>
    /// <returns>Migrated contract</returns>
    static abstract IVersionedContract MigrateFrom(TPrevious previous);
}

/// <summary>
/// Interface for contracts that support backward compatibility
/// </summary>
/// <typeparam name="TNext">Next version contract type</typeparam>
public interface IBackwardCompatibleContract<out TNext> : IVersionedContract
    where TNext : IVersionedContract
{
    /// <summary>
    /// Migrates this contract to a newer version
    /// </summary>
    /// <returns>Migrated contract</returns>
    TNext MigrateTo();
}