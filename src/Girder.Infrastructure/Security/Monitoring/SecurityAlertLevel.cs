namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Security alert severity levels
/// </summary>
public enum SecurityAlertLevel
{
    /// <summary>
    /// Informational - No action required
    /// </summary>
    Info = 0,

    /// <summary>
    /// Low priority - Monitor
    /// </summary>
    Low = 1,

    /// <summary>
    /// Medium priority - Review when possible
    /// </summary>
    Medium = 2,

    /// <summary>
    /// High priority - Investigate promptly
    /// </summary>
    High = 3,

    /// <summary>
    /// Critical - Immediate action required
    /// </summary>
    Critical = 4
}
