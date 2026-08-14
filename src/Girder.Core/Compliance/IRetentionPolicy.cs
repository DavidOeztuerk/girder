namespace Girder.Core.Compliance;

/// <summary>
/// Interface for data retention policy enforcement per DSGVO Art. 5(1)(e) (Storage Limitation).
/// Each service implements this to define and enforce its retention rules.
/// </summary>
/// <remarks>
/// Implementation requirements:
/// - Define retention periods for all PII-containing entities
/// - Implement automated purge logic (background job)
/// - Support legal holds (pause purging for specific users/records)
/// - Log all purge actions for compliance audit
///
/// See docs/compliance/retention-policy.md for the full retention schedule.
/// </remarks>
public interface IRetentionPolicy
{
    /// <summary>
    /// Get all retention rules defined for this service.
    /// </summary>
    /// <returns>List of retention rules</returns>
    IReadOnlyList<RetentionRule> GetRetentionRules();

    /// <summary>
    /// Execute retention purge for all entities in this service.
    /// Called by a scheduled background job (e.g., daily).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Report of purge actions taken</returns>
    Task<RetentionPurgeReport> ExecutePurgeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Place a legal hold on a specific user's data, preventing automatic purge.
    /// Required for data preservation during legal proceedings or investigations.
    /// </summary>
    /// <param name="userId">User whose data should be preserved</param>
    /// <param name="reason">Reason for the legal hold</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task PlaceLegalHoldAsync(string userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release a legal hold, allowing normal retention purge to resume.
    /// </summary>
    /// <param name="userId">User whose hold should be released</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReleaseLegalHoldAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a user is under legal hold.
    /// </summary>
    /// <param name="userId">User to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the user is under legal hold</returns>
    Task<bool> IsUnderLegalHoldAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a retention rule for a specific entity type
/// </summary>
public class RetentionRule
{
    /// <summary>
    /// Entity type this rule applies to (e.g., "UserLoginHistory", "E2EEAuditLog")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Service that owns this entity
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// How long data is retained after the trigger event
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; }

    /// <summary>
    /// What triggers the retention countdown (e.g., "CreatedAt", "ExpiresAt", "CompletedAt")
    /// </summary>
    public string RetentionTrigger { get; set; } = "CreatedAt";

    /// <summary>
    /// Action taken when retention period expires
    /// </summary>
    public RetentionAction Action { get; set; }

    /// <summary>
    /// Legal basis for this retention period
    /// </summary>
    public string LegalBasis { get; set; } = string.Empty;

    /// <summary>
    /// Whether this rule has a legal exception that extends retention
    /// (e.g., financial records under German tax law)
    /// </summary>
    public bool HasLegalException { get; set; }

    /// <summary>
    /// Description of the legal exception if applicable
    /// </summary>
    public string? LegalExceptionDescription { get; set; }
}

/// <summary>
/// Report of retention purge execution
/// </summary>
public class RetentionPurgeReport
{
    /// <summary>
    /// Service that executed the purge
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// When the purge was executed
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duration of the purge operation
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Results per entity type
    /// </summary>
    public List<EntityPurgeResult> EntityResults { get; set; } = new();

    /// <summary>
    /// Number of records skipped due to legal holds
    /// </summary>
    public int RecordsSkippedDueToLegalHold { get; set; }

    /// <summary>
    /// Errors encountered during purge
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Whether the purge completed successfully (no errors)
    /// </summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Purge result for a specific entity type
/// </summary>
public class EntityPurgeResult
{
    /// <summary>
    /// Entity type that was purged
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Number of records deleted
    /// </summary>
    public int RecordsDeleted { get; set; }

    /// <summary>
    /// Number of records anonymized
    /// </summary>
    public int RecordsAnonymized { get; set; }

    /// <summary>
    /// Number of records skipped (legal hold or not yet expired)
    /// </summary>
    public int RecordsSkipped { get; set; }
}

/// <summary>
/// Action taken when retention period expires
/// </summary>
public enum RetentionAction
{
    /// <summary>
    /// Permanently delete the record
    /// </summary>
    Delete,

    /// <summary>
    /// Anonymize PII fields but retain the record
    /// </summary>
    Anonymize,

    /// <summary>
    /// Archive to cold storage before deletion
    /// </summary>
    ArchiveThenDelete
}
