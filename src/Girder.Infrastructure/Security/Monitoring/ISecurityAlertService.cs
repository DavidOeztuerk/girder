namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Service for managing security alerts
/// </summary>
public interface ISecurityAlertService
{
    /// <summary>
    /// Send a new security alert
    /// </summary>
    Task<SecurityAlert> SendAlertAsync(
        SecurityAlertLevel level,
        SecurityAlertType type,
        string title,
        string message,
        Dictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get recent security alerts with pagination
    /// </summary>
    Task<(List<SecurityAlert> Alerts, int TotalCount)> GetRecentAlertsAsync(
        int pageNumber = 1,
        int pageSize = 50,
        SecurityAlertLevel? minLevel = null,
        SecurityAlertType? type = null,
        bool includeRead = true,
        bool includeDismissed = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get security alert by ID
    /// </summary>
    Task<SecurityAlert?> GetAlertByIdAsync(
        string alertId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get security alert statistics
    /// </summary>
    Task<SecurityAlertStatistics> GetAlertStatisticsAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark alert as read
    /// </summary>
    Task MarkAlertAsReadAsync(
        string alertId,
        string adminUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dismiss alert with reason
    /// </summary>
    Task DismissAlertAsync(
        string alertId,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk dismiss alerts
    /// </summary>
    Task BulkDismissAlertsAsync(
        List<string> alertIds,
        string adminUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get unread alert count
    /// </summary>
    Task<int> GetUnreadAlertCountAsync(
        SecurityAlertLevel? minLevel = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clean up old alerts (archive or delete)
    /// </summary>
    Task CleanupOldAlertsAsync(
        CancellationToken cancellationToken = default);
}
