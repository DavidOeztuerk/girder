namespace Girder.Infrastructure.Security.Compliance;

/// <summary>
/// Interface for GDPR and data protection compliance services
/// </summary>
public interface IDataProtectionService
{
    /// <summary>
    /// Process data subject access request (GDPR Article 15)
    /// </summary>
    // Task<DataSubjectAccessResult> ProcessAccessRequestAsync(DataSubjectAccessRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process data subject rectification request (GDPR Article 16)
    /// </summary>
    // Task<DataSubjectRectificationResult> ProcessRectificationRequestAsync(DataSubjectRectificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process data subject erasure request - "Right to be Forgotten" (GDPR Article 17)
    /// </summary>
    // Task<DataSubjectErasureResult> ProcessErasureRequestAsync(DataSubjectErasureRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process data portability request (GDPR Article 20)
    /// </summary>
    // Task<DataPortabilityResult> ProcessDataPortabilityRequestAsync(DataPortabilityRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process processing restriction request (GDPR Article 18)
    /// </summary>
    // Task<ProcessingRestrictionResult> ProcessRestrictionRequestAsync(ProcessingRestrictionRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Anonymize personal data
    /// </summary>
    // Task<AnonymizationResult> AnonymizeDataAsync(AnonymizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pseudonymize personal data
    /// </summary>
    // Task<PseudonymizationResult> PseudonymizeDataAsync(PseudonymizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assess data protection impact (DPIA)
    /// </summary>
    // Task<DataProtectionImpactAssessment> AssessDataProtectionImpactAsync(DataProcessingActivity activity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check consent status and validity
    /// </summary>
    Task<ConsentStatus> CheckConsentStatusAsync(string dataSubjectId, string processingPurpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record consent
    /// </summary>
    // Task<ConsentResult> RecordConsentAsync(ConsentRecord consent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraw consent
    /// </summary>
    // Task<ConsentWithdrawalResult> WithdrawConsentAsync(ConsentWithdrawalRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get data retention policy for specific data type
    /// </summary>
    Task<DataRetentionPolicy> GetDataRetentionPolicyAsync(string dataType, string processingPurpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply data retention policies — delete/anonymize data past retention period.
    /// DSGVO Art. 5(1)(e): Storage limitation principle.
    /// Called periodically by DataRetentionBackgroundService.
    /// </summary>
    Task<DataRetentionResult> ApplyDataRetentionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate compliance report — consent stats, retention status, pending requests.
    /// DSGVO Art. 30: Records of processing activities.
    /// Called periodically by ComplianceMonitoringBackgroundService.
    /// </summary>
    Task<ComplianceReport> GenerateComplianceReportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for consent management
/// </summary>
public interface IConsentManagementService
{
    /// <summary>
    /// Record new consent
    /// </summary>
    Task<string> RecordConsentAsync(ConsentRecord consent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing consent
    /// </summary>
    // Task UpdateConsentAsync(string consentId, ConsentUpdate update, CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraw consent
    /// </summary>
    // Task WithdrawConsentAsync(string consentId, ConsentWithdrawal withdrawal, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get consent history for data subject
    /// </summary>
    Task<List<ConsentRecord>> GetConsentHistoryAsync(string dataSubjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if specific processing is consented
    /// </summary>
    Task<bool> IsProcessingConsentedAsync(string dataSubjectId, string processingPurpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active consents for data subject
    /// </summary>
    Task<List<ConsentRecord>> GetActiveConsentsAsync(string dataSubjectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate consent requirements
    /// </summary>
    // Task<ConsentValidationResult> ValidateConsentRequirementsAsync(DataProcessingActivity activity, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for data breach notification
/// </summary>
public interface IDataBreachNotificationService
{
    /// <summary>
    /// Report data breach
    /// </summary>
    Task<string> ReportDataBreachAsync(DataBreach breach, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assess breach notification requirements
    /// </summary>
    // Task<BreachNotificationAssessment> AssessNotificationRequirementsAsync(DataBreach breach, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify supervisory authority (within 72 hours)
    /// </summary>
    // Task<NotificationResult> NotifySupervisoryAuthorityAsync(string breachId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notify affected data subjects
    /// </summary>
    // Task<NotificationResult> NotifyDataSubjectsAsync(string breachId, List<string> affectedSubjects, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update breach status
    /// </summary>
    Task UpdateBreachStatusAsync(string breachId, BreachStatus status, string statusReason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get breach report
    /// </summary>
    // Task<DataBreachReport> GetBreachReportAsync(string breachId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Data subject access request
/// </summary>
public class DataSubjectAccessRequest
{
    /// <summary>
    /// Data subject identifier
    /// </summary>
    public string DataSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Request identifier
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identity verification proof
    /// </summary>
    public IdentityVerification IdentityVerification { get; set; } = new();

    /// <summary>
    /// Specific data categories requested
    /// </summary>
    public List<string> RequestedDataCategories { get; set; } = new();

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Preferred response format
    /// </summary>
    public DataExportFormat PreferredFormat { get; set; } = DataExportFormat.JSON;

    /// <summary>
    /// Request source/channel
    /// </summary>
    public string RequestSource { get; set; } = string.Empty;

    /// <summary>
    /// Additional request details
    /// </summary>
    public Dictionary<string, string> AdditionalDetails { get; set; } = new();
}

/// <summary>
/// Data subject rectification request
/// </summary>
public class DataSubjectRectificationRequest
{
    /// <summary>
    /// Data subject identifier
    /// </summary>
    public string DataSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Request identifier
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identity verification proof
    /// </summary>
    public IdentityVerification IdentityVerification { get; set; } = new();

    /// <summary>
    /// Data corrections to be made
    /// </summary>
    public List<DataCorrection> Corrections { get; set; } = new();

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Justification for corrections
    /// </summary>
    public string Justification { get; set; } = string.Empty;
}

/// <summary>
/// Data subject erasure request
/// </summary>
public class DataSubjectErasureRequest
{
    /// <summary>
    /// Data subject identifier
    /// </summary>
    public string DataSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Request identifier
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identity verification proof
    /// </summary>
    public IdentityVerification IdentityVerification { get; set; } = new();

    /// <summary>
    /// Erasure grounds (GDPR Article 17)
    /// </summary>
    public List<ErasureGround> ErasureGrounds { get; set; } = new();

    /// <summary>
    /// Specific data categories to erase
    /// </summary>
    public List<string> DataCategoriesToErase { get; set; } = new();

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether to perform hard delete or soft delete
    /// </summary>
    public ErasureType ErasureType { get; set; } = ErasureType.SoftDelete;
}

/// <summary>
/// Data portability request
/// </summary>
public class DataPortabilityRequest
{
    /// <summary>
    /// Data subject identifier
    /// </summary>
    public string DataSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Request identifier
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identity verification proof
    /// </summary>
    public IdentityVerification IdentityVerification { get; set; } = new();

    /// <summary>
    /// Export format
    /// </summary>
    public DataExportFormat ExportFormat { get; set; } = DataExportFormat.JSON;

    /// <summary>
    /// Data categories to export
    /// </summary>
    public List<string> DataCategories { get; set; } = new();

    /// <summary>
    /// Target system (for direct transfer)
    /// </summary>
    public string? TargetSystem { get; set; }

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Processing restriction request
/// </summary>
public class ProcessingRestrictionRequest
{
    /// <summary>
    /// Data subject identifier
    /// </summary>
    public string DataSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Request identifier
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Identity verification proof
    /// </summary>
    public IdentityVerification IdentityVerification { get; set; } = new();

    /// <summary>
    /// Restriction grounds
    /// </summary>
    public List<RestrictionGround> RestrictionGrounds { get; set; } = new();

    /// <summary>
    /// Specific processing activities to restrict
    /// </summary>
    public List<string> ProcessingActivitiesToRestrict { get; set; } = new();

    /// <summary>
    /// Request timestamp
    /// </summary>
    public DateTime RequestTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Restriction period (if temporary)
    /// </summary>
    public TimeSpan? RestrictionPeriod { get; set; }
}

/// <summary>
/// Anonymization request
/// </summary>
public class AnonymizationRequest
{
    /// <summary>
    /// Data to anonymize
    /// </summary>
    public object Data { get; set; } = new();

    /// <summary>
    /// Anonymization technique
    /// </summary>
    public AnonymizationTechnique Technique { get; set; } = AnonymizationTechnique.KAnonymity;

    /// <summary>
    /// Anonymization parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Fields to anonymize
    /// </summary>
    public List<string> FieldsToAnonymize { get; set; } = new();

    /// <summary>
    /// Anonymization level
    /// </summary>
    public AnonymizationLevel Level { get; set; } = AnonymizationLevel.Medium;
}

/// <summary>
/// Pseudonymization request
/// </summary>
public class PseudonymizationRequest
{
    /// <summary>
    /// Data to pseudonymize
    /// </summary>
    public object Data { get; set; } = new();

    /// <summary>
    /// Pseudonymization technique
    /// </summary>
    public PseudonymizationTechnique Technique { get; set; } = PseudonymizationTechnique.Tokenization;

    /// <summary>
    /// Fields to pseudonymize
    /// </summary>
    public List<string> FieldsToPseudonymize { get; set; } = new();

    /// <summary>
    /// Whether pseudonymization should be reversible
    /// </summary>
    public bool Reversible { get; set; } = true;

    /// <summary>
    /// Pseudonym vault identifier
    /// </summary>
    public string? VaultId { get; set; }
}

/// <summary>
/// Data processing activity for DPIA
/// </summary>
public class DataProcessingActivity
{
    /// <summary>
    /// Activity identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Activity name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Activity description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Data controller
    /// </summary>
    // public DataController Controller { get; set; } = new();

    /// <summary>
    /// Data processors
    /// </summary>
    // public List<DataProcessor> Processors { get; set; } = new();

    /// <summary>
    /// Processing purposes
    /// </summary>
    // public List<ProcessingPurpose> Purposes { get; set; } = new();

    /// <summary>
    /// Data categories processed
    /// </summary>
    // public List<DataCategory> DataCategories { get; set; } = new();

    /// <summary>
    /// Data subjects categories
    /// </summary>
    // public List<DataSubjectCategory> DataSubjectCategories { get; set; } = new();

    /// <summary>
    /// Legal basis for processing
    /// </summary>
    // public List<LegalBasis> LegalBases { get; set; } = new();

    /// <summary>
    /// Data retention periods
    /// </summary>
    public Dictionary<string, TimeSpan> RetentionPeriods { get; set; } = new();

    /// <summary>
    /// Technical and organizational measures
    /// </summary>
    // public List<SecurityMeasure> SecurityMeasures { get; set; } = new();

    /// <summary>
    /// Cross-border transfers
    /// </summary>
    public List<DataTransfer> CrossBorderTransfers { get; set; } = new();

    /// <summary>
    /// Risk assessment
    /// </summary>
    // public RiskAssessment? RiskAssessment { get; set; }
}

/// <summary>
/// Consent record
/// </summary>
public class ConsentRecord
{
    /// <summary>
    /// Consent identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Data subject identifier
    /// </summary>
    public string DataSubjectId { get; set; } = string.Empty;

    /// <summary>
    /// Processing purpose
    /// </summary>
    public string ProcessingPurpose { get; set; } = string.Empty;

    /// <summary>
    /// Consent text shown to user
    /// </summary>
    public string ConsentText { get; set; } = string.Empty;

    /// <summary>
    /// Consent status
    /// </summary>
    public ConsentStatus Status { get; set; } = ConsentStatus.Given;

    /// <summary>
    /// Consent timestamp
    /// </summary>
    public DateTime ConsentTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Consent withdrawal timestamp
    /// </summary>
    public DateTime? WithdrawalTimestamp { get; set; }

    /// <summary>
    /// Consent method (web form, email, etc.)
    /// </summary>
    public string ConsentMethod { get; set; } = string.Empty;

    /// <summary>
    /// Consent version
    /// </summary>
    public string ConsentVersion { get; set; } = "1.0";

    /// <summary>
    /// Data categories covered by consent
    /// </summary>
    public List<string> DataCategories { get; set; } = new();

    /// <summary>
    /// Consent metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// IP address when consent was given
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent when consent was given
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Whether consent is freely given
    /// </summary>
    public bool FreelyGiven { get; set; } = true;

    /// <summary>
    /// Whether consent is specific
    /// </summary>
    public bool Specific { get; set; } = true;

    /// <summary>
    /// Whether consent is informed
    /// </summary>
    public bool Informed { get; set; } = true;

    /// <summary>
    /// Whether consent is unambiguous
    /// </summary>
    public bool Unambiguous { get; set; } = true;
}

/// <summary>
/// Data breach information
/// </summary>
public class DataBreach
{
    /// <summary>
    /// Breach identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Breach discovery timestamp
    /// </summary>
    public DateTime DiscoveryTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Breach occurrence timestamp (estimated)
    /// </summary>
    public DateTime? OccurrenceTimestamp { get; set; }

    /// <summary>
    /// Breach type
    /// </summary>
    public BreachType BreachType { get; set; } = BreachType.DataLoss;

    /// <summary>
    /// Breach description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Affected data categories
    /// </summary>
    public List<string> AffectedDataCategories { get; set; } = new();

    /// <summary>
    /// Number of affected data subjects
    /// </summary>
    public int AffectedDataSubjectsCount { get; set; }

    /// <summary>
    /// Affected data subject identifiers
    /// </summary>
    public List<string> AffectedDataSubjects { get; set; } = new();

    /// <summary>
    /// Likely consequences
    /// </summary>
    public List<string> LikelyConsequences { get; set; } = new();

    /// <summary>
    /// Measures taken to address breach
    /// </summary>
    public List<string> MeasuresTaken { get; set; } = new();

    /// <summary>
    /// Breach severity
    /// </summary>
    public BreachSeverity Severity { get; set; } = BreachSeverity.Medium;

    /// <summary>
    /// Risk assessment
    /// </summary>
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Medium;

    /// <summary>
    /// Breach status
    /// </summary>
    public BreachStatus Status { get; set; } = BreachStatus.Discovered;

    /// <summary>
    /// Reporting requirements
    /// </summary>
    // public BreachReportingRequirements ReportingRequirements { get; set; } = new();
}

/// <summary>
/// Data retention policy
/// </summary>
public class DataRetentionPolicy
{
    /// <summary>
    /// Policy identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Data type covered by policy
    /// </summary>
    public string DataType { get; set; } = string.Empty;

    /// <summary>
    /// Processing purpose
    /// </summary>
    public string ProcessingPurpose { get; set; } = string.Empty;

    /// <summary>
    /// Retention period
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; }

    /// <summary>
    /// Legal basis for retention
    /// </summary>
    public string LegalBasis { get; set; } = string.Empty;

    /// <summary>
    /// Retention conditions
    /// </summary>
    public List<string> RetentionConditions { get; set; } = new();

    /// <summary>
    /// Disposal method
    /// </summary>
    public DataDisposalMethod DisposalMethod { get; set; } = DataDisposalMethod.SecureDelete;

    /// <summary>
    /// Disposal verification required
    /// </summary>
    public bool RequireDisposalVerification { get; set; } = true;

    /// <summary>
    /// Policy creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Policy last updated timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Policy version
    /// </summary>
    public string Version { get; set; } = "1.0";
}

/// <summary>
/// Identity verification information
/// </summary>
public class IdentityVerification
{
    /// <summary>
    /// Verification method used
    /// </summary>
    public VerificationMethod Method { get; set; } = VerificationMethod.Email;

    /// <summary>
    /// Verification status
    /// </summary>
    public VerificationStatus Status { get; set; } = VerificationStatus.Pending;

    /// <summary>
    /// Verification timestamp
    /// </summary>
    public DateTime? VerificationTimestamp { get; set; }

    /// <summary>
    /// Verification reference/token
    /// </summary>
    public string? VerificationReference { get; set; }

    /// <summary>
    /// Additional verification data
    /// </summary>
    public Dictionary<string, string> VerificationData { get; set; } = new();
}

/// <summary>
/// Data correction information
/// </summary>
public class DataCorrection
{
    /// <summary>
    /// Field name to correct
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Current value
    /// </summary>
    public string? CurrentValue { get; set; }

    /// <summary>
    /// Corrected value
    /// </summary>
    public string CorrectedValue { get; set; } = string.Empty;

    /// <summary>
    /// Correction reason
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Enumerations for GDPR compliance
/// </summary>
public enum DataExportFormat
{
    JSON,
    XML,
    CSV,
    PDF
}

public enum ErasureGround
{
    DataNoLongerNecessary,
    ConsentWithdrawn,
    UnlawfulProcessing,
    LegalCompliance,
    ObjectionToProcessing,
    DirectMarketing
}

public enum ErasureType
{
    SoftDelete,
    HardDelete,
    Anonymization
}

public enum RestrictionGround
{
    AccuracyContested,
    UnlawfulProcessing,
    NoLongerNeeded,
    ObjectionPending
}

public enum AnonymizationTechnique
{
    KAnonymity,
    LDiversity,
    TCloseness,
    DifferentialPrivacy,
    DataMasking,
    Generalization,
    Suppression
}

public enum AnonymizationLevel
{
    Low,
    Medium,
    High,
    Maximum
}

public enum PseudonymizationTechnique
{
    Tokenization,
    Encryption,
    Hashing,
    FormatPreservingEncryption
}

public enum ConsentStatus
{
    Given,
    Withdrawn,
    Expired,
    Invalid
}

public enum BreachType
{
    DataLoss,
    DataTheft,
    UnauthorizedAccess,
    UnauthorizedDisclosure,
    DataCorruption,
    SystemCompromise
}

public enum BreachSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    VeryHigh
}

public enum BreachStatus
{
    Discovered,
    Investigated,
    Contained,
    Reported,
    Resolved,
    Closed
}

public enum DataDisposalMethod
{
    SecureDelete,
    Overwrite,
    PhysicalDestruction,
    Anonymization,
    Encryption
}

public enum VerificationMethod
{
    Email,
    SMS,
    Phone,
    InPerson,
    DocumentUpload,
    TwoFactor
}

public enum VerificationStatus
{
    Pending,
    Verified,
    Failed,
    Expired
}

/// <summary>
/// Result of automated data retention execution.
/// DSGVO Art. 5(1)(e): Storage limitation.
/// </summary>
public class DataRetentionResult
{
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public int ExpiredConsentsMarked { get; set; }
    public int ExpiredTokensRemoved { get; set; }
    public int AuditLogsArchived { get; set; }
    public int DeletedAccountsCleaned { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Periodic compliance report.
/// DSGVO Art. 30: Records of processing activities.
/// </summary>
public class ComplianceReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public int ActiveConsentsCount { get; set; }
    public int ExpiredConsentsCount { get; set; }
    public int WithdrawnConsentsCount { get; set; }
    public int PendingDeletionRequests { get; set; }
    public int DataBreachesOpen { get; set; }
    public int DataBreachesResolved { get; set; }
    public DataRetentionResult? LastRetentionRun { get; set; }
    public List<string> Warnings { get; set; } = new();
}