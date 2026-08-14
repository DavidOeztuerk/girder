namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Statistics for security alerts
/// </summary>
public class SecurityAlertStatistics
{
    public int TotalAlerts { get; set; }

    public int CriticalAlerts { get; set; }

    public int HighAlerts { get; set; }

    public int MediumAlerts { get; set; }

    public int LowAlerts { get; set; }

    public int InfoAlerts { get; set; }

    public int UnreadAlerts { get; set; }

    public int ActiveAlerts { get; set; }

    public int DismissedAlerts { get; set; }

    public DateTime? LastCriticalAlertAt { get; set; }

    public DateTime? LastAlertAt { get; set; }

    public List<AlertTypeCount> AlertsByType { get; set; } = new();

    public List<AlertTimelinePoint> Timeline { get; set; } = new();
}

public class AlertTypeCount
{
    public SecurityAlertType Type { get; set; }

    public int Count { get; set; }

    public SecurityAlertLevel HighestSeverity { get; set; }
}

public class AlertTimelinePoint
{
    public DateTime Date { get; set; }

    public int Critical { get; set; }

    public int High { get; set; }

    public int Medium { get; set; }

    public int Low { get; set; }

    public int Info { get; set; }
}
