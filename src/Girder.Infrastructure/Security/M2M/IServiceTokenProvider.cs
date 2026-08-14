namespace Infrastructure.Security.M2M;

/// <summary>
/// Provider for service-to-service authentication tokens
/// </summary>
public interface IServiceTokenProvider
{
    /// <summary>
    /// Get a valid access token for service-to-service communication
    /// </summary>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Force refresh the current token
    /// </summary>
    Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidate the current cached token
    /// </summary>
    Task InvalidateTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get token information
    /// </summary>
    Task<TokenInfo?> GetTokenInfoAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about an access token
/// </summary>
public class TokenInfo
{
    /// <summary>
    /// Access token
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Token type (usually "Bearer")
    /// </summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>
    /// When the token expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Token scopes
    /// </summary>
    public HashSet<string> Scopes { get; set; } = new();

    /// <summary>
    /// Whether the token is still valid
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt;

    /// <summary>
    /// Whether the token should be refreshed soon
    /// </summary>
    public bool ShouldRefresh(TimeSpan refreshWindow) => DateTime.UtcNow >= ExpiresAt.Subtract(refreshWindow);
}
