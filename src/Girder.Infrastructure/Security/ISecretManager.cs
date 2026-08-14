namespace Infrastructure.Security;

/// <summary>
/// Interface for secure secret management
/// </summary>
public interface ISecretManager
{
    /// <summary>
    /// Get a secret by name
    /// </summary>
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set a secret by name
    /// </summary>
    Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotate a secret
    /// </summary>
    Task<string> RotateSecretAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get secret history for rotation
    /// </summary>
    Task<IEnumerable<SecretVersion>> GetSecretHistoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a secret
    /// </summary>
    Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a secret exists
    /// </summary>
    Task<bool> SecretExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all secret names
    /// </summary>
    Task<IEnumerable<string>> GetSecretNamesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Secret version information
/// </summary>
public class SecretVersion
{
    /// <summary>
    /// Secret name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Secret value (encrypted)
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Version number
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Expiration timestamp
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Whether this version is active
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Who created this version
    /// </summary>
    public string CreatedBy { get; set; } = "System";
}