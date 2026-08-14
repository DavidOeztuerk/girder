namespace Core.Common.Compliance;

/// <summary>
/// Service-level interface for DSGVO Art. 20 (Right to Data Portability) compliance.
/// Each microservice implements this to export its portable user data.
/// </summary>
/// <remarks>
/// This interface defines the contract that each service must implement to contribute
/// its portion of user data to the aggregated data export.
///
/// Only data that meets Art. 20 portability criteria is exported:
/// - Provided by the data subject (directly or observed)
/// - Processed on the basis of consent (Art. 6(1)(a)) or contract (Art. 6(1)(b))
/// - Processed by automated means
///
/// Data NOT eligible for export:
/// - Data processed under legitimate interest (Art. 6(1)(f)) — e.g., security logs
/// - Inferred or derived data — e.g., match scores
/// - System metadata — e.g., CreatedAt, UpdatedAt, IsDeleted
///
/// See docs/compliance/right-to-portability.md for the full export specification.
/// </remarks>
public interface IDataExportService
{
    /// <summary>
    /// Export all portable user data from this service in the specified format.
    /// </summary>
    /// <param name="userId">The user ID whose data to export</param>
    /// <param name="format">Export format (JSON or CSV)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Export result containing the data and metadata</returns>
    Task<ServiceDataExport> ExportUserDataAsync(
        string userId,
        ExportFormat format = ExportFormat.Json,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the list of data categories this service can export for a user.
    /// Used to build the export metadata before actual export.
    /// </summary>
    /// <param name="userId">The user ID to query</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Available data categories and record counts</returns>
    Task<ExportableDataSummary> GetExportableDataSummaryAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Data export result from a single service
/// </summary>
public class ServiceDataExport
{
    /// <summary>
    /// Service that produced this export
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// User ID the export belongs to
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Export format used
    /// </summary>
    public ExportFormat Format { get; set; }

    /// <summary>
    /// Serialized data content (JSON string or CSV string)
    /// </summary>
    public string DataContent { get; set; } = string.Empty;

    /// <summary>
    /// Binary file attachments (profile pictures, documents).
    /// Key = filename, Value = file bytes.
    /// </summary>
    public Dictionary<string, byte[]> FileAttachments { get; set; } = new();

    /// <summary>
    /// Number of records exported
    /// </summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// Export timestamp
    /// </summary>
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the export completed successfully
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if export failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Summary of exportable data categories for a user
/// </summary>
public class ExportableDataSummary
{
    /// <summary>
    /// Service name
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// Data categories available for export with record counts
    /// </summary>
    public List<ExportableCategory> Categories { get; set; } = new();
}

/// <summary>
/// A single exportable data category
/// </summary>
public class ExportableCategory
{
    /// <summary>
    /// Category name (e.g., "SkillListings", "ChatMessages", "Appointments")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Number of records available for export
    /// </summary>
    public int RecordCount { get; set; }

    /// <summary>
    /// Whether this category includes file attachments
    /// </summary>
    public bool HasFileAttachments { get; set; }
}

/// <summary>
/// Supported export formats
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// JSON format (primary, machine-readable)
    /// </summary>
    Json,

    /// <summary>
    /// CSV format (secondary, tabular)
    /// </summary>
    Csv
}
