namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Configuration for security alert service
/// </summary>
public class SecurityAlertConfiguration
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of recent alerts to keep in Redis cache
    /// </summary>
    public int MaxRecentAlertsInCache { get; set; } = 1000;

    /// <summary>
    /// Number of days to retain alerts in database before archival
    /// </summary>
    public int RetentionDays { get; set; } = 60;

    /// <summary>
    /// Enable email notifications for critical alerts
    /// </summary>
    public bool EnableEmailNotifications { get; set; } = false;

    /// <summary>
    /// Email addresses to notify for critical alerts
    /// </summary>
    public List<string> NotificationEmails { get; set; } = new();

    /// <summary>
    /// Minimum time between duplicate alerts (in seconds)
    /// </summary>
    public int DuplicateAlertThrottleSeconds { get; set; } = 300; // 5 minutes

    /// <summary>
    /// Enable alert aggregation (group similar alerts)
    /// </summary>
    public bool EnableAlertAggregation { get; set; } = true;

    /// <summary>
    /// Alert level thresholds for different actions
    /// </summary>
    public AlertThresholds Thresholds { get; set; } = new();
}

public class AlertThresholds
{
    /// <summary>
    /// Auto-block IP after this many critical alerts from same IP
    /// </summary>
    public int AutoBlockIPAfterCriticalAlerts { get; set; } = 5;

    /// <summary>
    /// Auto-suspend user after this many high-severity alerts
    /// </summary>
    public int AutoSuspendUserAfterHighAlerts { get; set; } = 10;

    /// <summary>
    /// Maximum alerts per IP per hour before considering DDoS
    /// </summary>
    public int MaxAlertsPerIPPerHour { get; set; } = 100;
}
