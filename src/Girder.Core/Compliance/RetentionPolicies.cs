namespace Girder.Core.Compliance;

/// <summary>
/// DSGVO-compliant data retention periods. Used by background cleanup jobs.
/// </summary>
public static class RetentionPolicies
{
    public static readonly TimeSpan UserActivity = TimeSpan.FromDays(365);
    public static readonly TimeSpan UserSessions = TimeSpan.FromDays(365);
    public static readonly TimeSpan LoginHistory = TimeSpan.FromDays(365);
    public static readonly TimeSpan RefreshTokens = TimeSpan.FromDays(30);
    public static readonly TimeSpan AuditLogs = TimeSpan.FromDays(180);
    public static readonly TimeSpan CallRecordings = TimeSpan.FromDays(90);
}
