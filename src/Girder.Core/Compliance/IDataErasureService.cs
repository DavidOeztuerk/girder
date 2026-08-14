namespace Core.Common.Compliance;

/// <summary>
/// Service-level interface for DSGVO Art. 17 (Right to Erasure) compliance.
/// Each microservice implements this to handle its own data erasure responsibilities.
/// </summary>
/// <remarks>
/// This interface defines the contract that each service must implement to participate
/// in the distributed erasure cascade triggered by UserDeletedEvent.
///
/// Implementation requirements:
/// - Hard delete data where no legal retention obligation exists
/// - Anonymize data where retention is legally required (e.g., financial records)
/// - Clean up blob storage for file references (profile pictures, documents, recordings)
/// - Invalidate caches containing user PII
/// - Return a complete report of actions taken for audit trail
///
/// See docs/compliance/right-to-erasure.md for the full erasure cascade specification.
/// </remarks>
public interface IDataErasureService
{
    /// <summary>
    /// Erase all personal data for the specified user within this service.
    /// Called as part of the distributed erasure cascade.
    /// </summary>
    /// <param name="userId">The user ID whose data must be erased</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Report of erasure actions taken</returns>
    Task<ErasureReport> EraseUserDataAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a summary of all data held for the specified user within this service.
    /// Used to inform the user before erasure (Art. 15 access request support).
    /// </summary>
    /// <param name="userId">The user ID to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of data held for this user</returns>
    Task<UserDataSummary> GetUserDataSummaryAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Report of erasure actions taken by a service
/// </summary>
public class ErasureReport
{
    /// <summary>
    /// Service that performed the erasure
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// User ID that was erased
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Whether all erasure actions completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Timestamp of erasure completion
    /// </summary>
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Individual entity erasure results
    /// </summary>
    public List<EntityErasureResult> EntityResults { get; set; } = new();

    /// <summary>
    /// Data that was retained due to legal obligation (e.g., financial records)
    /// </summary>
    public List<RetainedDataRecord> RetainedData { get; set; } = new();

    /// <summary>
    /// Errors encountered during erasure
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of erasing a specific entity type
/// </summary>
public class EntityErasureResult
{
    /// <summary>
    /// Entity type name (e.g., "ChatMessage", "VideoCallSession")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Action taken: Deleted, Anonymized, or Skipped
    /// </summary>
    public ErasureAction Action { get; set; }

    /// <summary>
    /// Number of records affected
    /// </summary>
    public int RecordsAffected { get; set; }
}

/// <summary>
/// Record of data retained due to legal obligation
/// </summary>
public class RetainedDataRecord
{
    /// <summary>
    /// Entity type retained
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Legal basis for retention (e.g., "AO §147 — 10 year tax retention")
    /// </summary>
    public string LegalBasis { get; set; } = string.Empty;

    /// <summary>
    /// What anonymization was applied to the retained data
    /// </summary>
    public string AnonymizationApplied { get; set; } = string.Empty;

    /// <summary>
    /// When the retained data will be eligible for final deletion
    /// </summary>
    public DateTime? RetainUntil { get; set; }
}

/// <summary>
/// Summary of data held for a user within a service
/// </summary>
public class UserDataSummary
{
    /// <summary>
    /// Service name
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Entity types and record counts
    /// </summary>
    public Dictionary<string, int> EntityCounts { get; set; } = new();

    /// <summary>
    /// Whether blob storage files exist for this user
    /// </summary>
    public bool HasBlobStorageFiles { get; set; }
}

/// <summary>
/// Type of erasure action taken
/// </summary>
public enum ErasureAction
{
    /// <summary>
    /// Records permanently deleted
    /// </summary>
    Deleted,

    /// <summary>
    /// Records anonymized (user identity removed, data preserved)
    /// </summary>
    Anonymized,

    /// <summary>
    /// Records skipped (no data found or not applicable)
    /// </summary>
    Skipped
}
