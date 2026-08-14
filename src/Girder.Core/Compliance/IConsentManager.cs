namespace Core.Common.Compliance;

/// <summary>
/// Interface for managing user consent in compliance with DSGVO Art. 6(1)(a) and Art. 7.
/// Implemented by UserService as the central consent authority.
/// </summary>
/// <remarks>
/// Consent management requirements:
/// - Consent must be freely given, specific, informed, and unambiguous (Art. 7)
/// - Controller must be able to demonstrate that consent was given (Art. 7(1))
/// - Withdrawal must be as easy as giving consent (Art. 7(3))
/// - Consent records must be retained for 3 years after account deletion
///
/// See docs/compliance/consent-management.md for consent categories and collection points.
/// </remarks>
public interface IConsentManager
{
    /// <summary>
    /// Record that a user has granted consent for a specific purpose.
    /// </summary>
    /// <param name="request">Consent grant details</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The recorded consent ID</returns>
    Task<string> GrantConsentAsync(ConsentGrantRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraw previously granted consent.
    /// Must trigger data deletion where consent was the sole legal basis.
    /// </summary>
    /// <param name="userId">User withdrawing consent</param>
    /// <param name="consentId">Consent category identifier (e.g., "oauth.linkedin")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Withdrawal result including any data deletion triggered</returns>
    Task<ConsentWithdrawalResult> WithdrawConsentAsync(
        string userId,
        string consentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user has active consent for a specific purpose.
    /// </summary>
    /// <param name="userId">User to check</param>
    /// <param name="consentId">Consent category identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if active consent exists</returns>
    Task<bool> HasConsentAsync(string userId, string consentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active consents for a user.
    /// Used to display consent status in user settings.
    /// </summary>
    /// <param name="userId">User to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active consent records</returns>
    Task<IReadOnlyList<ConsentInfo>> GetActiveConsentsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the full consent audit trail for a user.
    /// Used for Art. 7(1) demonstrability and compliance audits.
    /// </summary>
    /// <param name="userId">User to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Complete consent history</returns>
    Task<IReadOnlyList<ConsentAuditEntry>> GetConsentAuditTrailAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to record a consent grant
/// </summary>
public class ConsentGrantRequest
{
    /// <summary>
    /// User granting consent
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Consent category identifier (e.g., "oauth.linkedin", "device.fingerprinting")
    /// </summary>
    public string ConsentId { get; set; } = string.Empty;

    /// <summary>
    /// Version of the consent text shown to the user
    /// </summary>
    public string ConsentVersion { get; set; } = "1.0";

    /// <summary>
    /// How consent was collected (e.g., "explicit_checkbox", "oauth_flow_initiation", "settings_toggle")
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Client IP address at time of consent
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Client user agent at time of consent
    /// </summary>
    public string? UserAgent { get; set; }
}

/// <summary>
/// Result of consent withdrawal
/// </summary>
public class ConsentWithdrawalResult
{
    /// <summary>
    /// Whether withdrawal was processed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Whether data deletion was triggered as a result of withdrawal
    /// </summary>
    public bool DataDeletionTriggered { get; set; }

    /// <summary>
    /// Description of data deletion actions taken
    /// </summary>
    public string? DataDeletionDetails { get; set; }

    /// <summary>
    /// Timestamp of withdrawal
    /// </summary>
    public DateTime WithdrawnAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Active consent information for display
/// </summary>
public class ConsentInfo
{
    /// <summary>
    /// Consent category identifier
    /// </summary>
    public string ConsentId { get; set; } = string.Empty;

    /// <summary>
    /// When consent was granted
    /// </summary>
    public DateTime GrantedAt { get; set; }

    /// <summary>
    /// Version of consent text
    /// </summary>
    public string ConsentVersion { get; set; } = string.Empty;

    /// <summary>
    /// How consent was collected
    /// </summary>
    public string Method { get; set; } = string.Empty;
}

/// <summary>
/// Consent audit trail entry for compliance demonstration
/// </summary>
public class ConsentAuditEntry
{
    /// <summary>
    /// Consent category identifier
    /// </summary>
    public string ConsentId { get; set; } = string.Empty;

    /// <summary>
    /// Action taken: Granted, Withdrawn, Renewed
    /// </summary>
    public ConsentAction Action { get; set; }

    /// <summary>
    /// When the action occurred
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Consent text version
    /// </summary>
    public string ConsentVersion { get; set; } = string.Empty;

    /// <summary>
    /// How the action was performed
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// IP address at time of action
    /// </summary>
    public string? IpAddress { get; set; }
}

/// <summary>
/// Consent audit action type
/// </summary>
public enum ConsentAction
{
    /// <summary>
    /// Consent granted by user
    /// </summary>
    Granted,

    /// <summary>
    /// Consent withdrawn by user
    /// </summary>
    Withdrawn,

    /// <summary>
    /// Consent renewed (re-confirmed after terms change)
    /// </summary>
    Renewed
}
