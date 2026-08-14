namespace Infrastructure.Security.Secrets;

/// <summary>
/// Interface for secret providers (Vault, Azure Key Vault, etc.)
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Gets a secret by key
    /// </summary>
    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Sets a secret
    /// </summary>
    Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Deletes a secret
    /// </summary>
    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if a secret exists
    /// </summary>
    Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all secret keys (without values)
    /// </summary>
    Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Extended interface for providers that support versioning
/// </summary>
public interface IVersionedSecretProvider : ISecretProvider
{
    /// <summary>
    /// Gets a specific version of a secret
    /// </summary>
    Task<string?> GetSecretVersionAsync(string key, string version, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Lists all versions of a secret
    /// </summary>
    Task<IEnumerable<SecretVersion>> ListSecretVersionsAsync(string key, CancellationToken cancellationToken = default);
}

// SecretVersion is defined in Infrastructure.Security.ISecretManager

/// <summary>
/// Secret metadata
/// </summary>
public record SecretMetadata(
    string Key,
    DateTime CreatedAt,
    DateTime? LastModified,
    DateTime? ExpiresAt,
    Dictionary<string, string> Tags);