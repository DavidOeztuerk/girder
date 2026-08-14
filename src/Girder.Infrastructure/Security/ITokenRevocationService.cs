namespace Infrastructure.Security;

/// <summary>
/// Interface for token revocation service
/// </summary>
public interface ITokenRevocationService
{
    /// <summary>
    /// Revoke a token by JTI (JWT ID)
    /// </summary>
    Task RevokeTokenAsync(string jti, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke all tokens for a user
    /// </summary>
    Task RevokeUserTokensAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a token is revoked
    /// </summary>
    Task<bool> IsTokenRevokedAsync(string jti, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke token by refresh token
    /// </summary>
    Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if refresh token is revoked
    /// </summary>
    Task<bool> IsRefreshTokenRevokedAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all revoked tokens for a user (for admin purposes)
    /// </summary>
    Task<IEnumerable<RevokedTokenInfo>> GetRevokedTokensAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up expired revoked tokens
    /// </summary>
    Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke token with reason and metadata
    /// </summary>
    Task RevokeTokenAsync(TokenRevocationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a revoked token
/// </summary>
public class RevokedTokenInfo
{
    /// <summary>
    /// JWT ID
    /// </summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>
    /// User ID associated with the token
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// When the token was revoked
    /// </summary>
    public DateTime RevokedAt { get; set; }

    /// <summary>
    /// Reason for revocation
    /// </summary>
    public TokenRevocationReason Reason { get; set; }

    /// <summary>
    /// Additional details about the revocation
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// IP address from which revocation was initiated
    /// </summary>
    public string? RevokedFromIp { get; set; }

    /// <summary>
    /// User agent from which revocation was initiated
    /// </summary>
    public string? RevokedFromUserAgent { get; set; }

    /// <summary>
    /// When the token expires (for cleanup purposes)
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Token revocation request
/// </summary>
public class TokenRevocationRequest
{
    /// <summary>
    /// JWT ID to revoke
    /// </summary>
    public string Jti { get; set; } = string.Empty;

    /// <summary>
    /// User ID associated with the token
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Reason for revocation
    /// </summary>
    public TokenRevocationReason Reason { get; set; }

    /// <summary>
    /// Additional details about the revocation
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// IP address from which revocation was initiated
    /// </summary>
    public string? RevokedFromIp { get; set; }

    /// <summary>
    /// User agent from which revocation was initiated
    /// </summary>
    public string? RevokedFromUserAgent { get; set; }

    /// <summary>
    /// Token expiry time (for automatic cleanup)
    /// </summary>
    public TimeSpan? TokenExpiry { get; set; }
}

/// <summary>
/// Reasons for token revocation
/// </summary>
public enum TokenRevocationReason
{
    /// <summary>
    /// User manually logged out
    /// </summary>
    UserLogout,

    /// <summary>
    /// Administrative action
    /// </summary>
    AdminRevocation,

    /// <summary>
    /// Security incident detected
    /// </summary>
    SecurityIncident,

    /// <summary>
    /// Password changed
    /// </summary>
    PasswordChanged,

    /// <summary>
    /// Account suspended
    /// </summary>
    AccountSuspended,

    /// <summary>
    /// Token rotation
    /// </summary>
    TokenRotation,

    /// <summary>
    /// Suspicious activity detected
    /// </summary>
    SuspiciousActivity,

    /// <summary>
    /// User requested token revocation
    /// </summary>
    UserRequested,

    /// <summary>
    /// System maintenance
    /// </summary>
    SystemMaintenance
}