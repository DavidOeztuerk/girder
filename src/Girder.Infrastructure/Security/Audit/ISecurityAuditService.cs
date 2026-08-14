namespace Infrastructure.Security.Audit;

/// <summary>
/// Interface for tamper-proof security audit logging
/// </summary>
public interface ISecurityAuditService
{
    /// <summary>
    /// Log a security audit event
    /// </summary>
    Task<string> LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Log a security audit event with automatic context extraction
    /// </summary>
    Task<string> LogSecurityEventAsync(
        string eventType, 
        string description,
        SecurityEventSeverity severity = SecurityEventSeverity.Information,
        object? additionalData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get security audit events
    /// </summary>
    Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(
        SecurityAuditQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify the integrity of audit logs
    /// </summary>
    Task<AuditIntegrityResult> VerifyAuditIntegrityAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate audit report
    /// </summary>
    Task<SecurityAuditReport> GenerateAuditReportAsync(
        SecurityAuditQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Export audit logs for compliance
    /// </summary>
    Task<byte[]> ExportAuditLogsAsync(
        SecurityAuditQuery query,
        AuditExportFormat format = AuditExportFormat.Json,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Archive old audit logs
    /// </summary>
    Task<int> ArchiveOldLogsAsync(
        DateTime archiveBeforeDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get audit statistics
    /// </summary>
    Task<SecurityAuditStatistics> GetAuditStatisticsAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Enhanced security audit event with tamper-proof features
/// </summary>
public class SecurityAuditEvent
{
    /// <summary>
    /// Unique event ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Event type/category
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Event description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// User ID associated with the event
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Session ID
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Source IP address
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Request ID for correlation
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Source service/component
    /// </summary>
    public string Source { get; set; } = "Skillswap";

    /// <summary>
    /// Event timestamp (UTC)
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Event severity level
    /// </summary>
    public SecurityEventSeverity Severity { get; set; } = SecurityEventSeverity.Information;

    /// <summary>
    /// Event category
    /// </summary>
    public SecurityEventCategory Category { get; set; } = SecurityEventCategory.General;

    /// <summary>
    /// Resource affected by the event
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Resource ID
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Action performed
    /// </summary>
    public string? Action { get; set; }

    /// <summary>
    /// Result of the action
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Additional structured data
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();

    /// <summary>
    /// Risk score (0-100)
    /// </summary>
    public int RiskScore { get; set; }

    /// <summary>
    /// Event tags for categorization
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Hash of the previous event for chain integrity
    /// </summary>
    public string? PreviousEventHash { get; set; }

    /// <summary>
    /// Hash of this event for integrity verification
    /// </summary>
    public string? EventHash { get; set; }

    /// <summary>
    /// Digital signature for tamper detection
    /// </summary>
    public string? Signature { get; set; }

    /// <summary>
    /// Compliance flags (GDPR, SOX, etc.)
    /// </summary>
    public List<string> ComplianceFlags { get; set; } = new();

    /// <summary>
    /// Retention period in days
    /// </summary>
    public int RetentionDays { get; set; } = 2555; // ~7 years default
}

/// <summary>
/// Security event severity levels
/// </summary>
public enum SecurityEventSeverity
{
    /// <summary>
    /// Informational event
    /// </summary>
    Information = 0,

    /// <summary>
    /// Low severity event
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium severity event
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High severity event
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical security event
    /// </summary>
    Critical = 4
}

/// <summary>
/// Security event categories
/// </summary>
public enum SecurityEventCategory
{
    General,
    Authentication,
    Authorization,
    DataAccess,
    DataModification,
    ConfigurationChange,
    SystemEvent,
    SecurityIncident,
    ComplianceEvent,
    PerformanceEvent
}

/// <summary>
/// Query parameters for security audit events
/// </summary>
public class SecurityAuditQuery
{
    /// <summary>
    /// Start date filter
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// End date filter
    /// </summary>
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// User ID filter
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Event type filter
    /// </summary>
    public string? EventType { get; set; }

    /// <summary>
    /// Severity filter
    /// </summary>
    public SecurityEventSeverity? Severity { get; set; }

    /// <summary>
    /// Category filter
    /// </summary>
    public SecurityEventCategory? Category { get; set; }

    /// <summary>
    /// Source filter
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// IP address filter
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// Resource type filter
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Resource ID filter
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Tags filter
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Free text search
    /// </summary>
    public string? SearchText { get; set; }

    /// <summary>
    /// Page number for pagination
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size for pagination
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// Sort field
    /// </summary>
    public string SortBy { get; set; } = "Timestamp";

    /// <summary>
    /// Sort direction
    /// </summary>
    public bool SortDescending { get; set; } = true;
}

/// <summary>
/// Audit integrity verification result
/// </summary>
public class AuditIntegrityResult
{
    /// <summary>
    /// Whether the audit log chain is intact
    /// </summary>
    public bool IsIntegrityIntact { get; set; }

    /// <summary>
    /// Number of events verified
    /// </summary>
    public int EventsVerified { get; set; }

    /// <summary>
    /// Number of integrity violations found
    /// </summary>
    public int IntegrityViolations { get; set; }

    /// <summary>
    /// Details of integrity violations
    /// </summary>
    public List<IntegrityViolation> Violations { get; set; } = new();

    /// <summary>
    /// Verification timestamp
    /// </summary>
    public DateTime VerificationTimestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Integrity violation details
/// </summary>
public class IntegrityViolation
{
    /// <summary>
    /// Event ID where violation was detected
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    /// Type of violation
    /// </summary>
    public string ViolationType { get; set; } = string.Empty;

    /// <summary>
    /// Description of the violation
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the violated event
    /// </summary>
    public DateTime EventTimestamp { get; set; }
}

/// <summary>
/// Security audit report
/// </summary>
public class SecurityAuditReport
{
    /// <summary>
    /// Report ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Report generation timestamp
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Report period start
    /// </summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>
    /// Report period end
    /// </summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>
    /// Total events in period
    /// </summary>
    public int TotalEvents { get; set; }

    /// <summary>
    /// Events by severity
    /// </summary>
    public Dictionary<SecurityEventSeverity, int> EventsBySeverity { get; set; } = new();

    /// <summary>
    /// Events by category
    /// </summary>
    public Dictionary<SecurityEventCategory, int> EventsByCategory { get; set; } = new();

    /// <summary>
    /// Top users by event count
    /// </summary>
    public Dictionary<string, int> TopUsersByEventCount { get; set; } = new();

    /// <summary>
    /// Top IP addresses by event count
    /// </summary>
    public Dictionary<string, int> TopIpAddressesByEventCount { get; set; } = new();

    /// <summary>
    /// Security incidents
    /// </summary>
    public List<SecurityAuditEvent> SecurityIncidents { get; set; } = new();

    /// <summary>
    /// Compliance summary
    /// </summary>
    public Dictionary<string, int> ComplianceSummary { get; set; } = new();
}

/// <summary>
/// Security audit statistics
/// </summary>
public class SecurityAuditStatistics
{
    /// <summary>
    /// Total events logged
    /// </summary>
    public long TotalEvents { get; set; }

    /// <summary>
    /// Events per day
    /// </summary>
    public Dictionary<DateTime, int> EventsPerDay { get; set; } = new();

    /// <summary>
    /// Average events per day
    /// </summary>
    public double AverageEventsPerDay { get; set; }

    /// <summary>
    /// Storage size in bytes
    /// </summary>
    public long StorageSizeBytes { get; set; }

    /// <summary>
    /// Oldest event timestamp
    /// </summary>
    public DateTime? OldestEventTimestamp { get; set; }

    /// <summary>
    /// Newest event timestamp
    /// </summary>
    public DateTime? NewestEventTimestamp { get; set; }
}

/// <summary>
/// Audit export formats
/// </summary>
public enum AuditExportFormat
{
    Json,
    Csv,
    Xml,
    Pdf
}