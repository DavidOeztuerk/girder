namespace Infrastructure.Communication.Configuration;

/// <summary>
/// Machine-to-Machine authentication configuration
/// </summary>
public class M2MConfiguration
{
    /// <summary>
    /// Enable M2M authentication
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Token endpoint for client credentials flow
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// Client ID for this service
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client secret for this service
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Scopes to request
    /// </summary>
    public HashSet<string> Scopes { get; set; } = new() { "api.read", "api.write" };

    /// <summary>
    /// Token lifetime
    /// </summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Refresh token before expiry
    /// </summary>
    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Enable token caching
    /// </summary>
    public bool EnableTokenCaching { get; set; } = true;

    /// <summary>
    /// Token cache key prefix
    /// </summary>
    public string TokenCacheKeyPrefix { get; set; } = "m2m:token:";

    /// <summary>
    /// Use mutual TLS for token requests
    /// </summary>
    public bool UseMutualTLS { get; set; } = false;

    /// <summary>
    /// Client certificate path (for mTLS)
    /// </summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>
    /// Client certificate password (for mTLS)
    /// </summary>
    public string? ClientCertificatePassword { get; set; }
}
