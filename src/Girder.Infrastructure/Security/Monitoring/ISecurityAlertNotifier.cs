namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Interface for security alert notifications.
/// Allows Infrastructure layer to request notifications without depending on Events/MassTransit.
/// Actual implementation should be provided by application layer via DI.
/// </summary>
public interface ISecurityAlertNotifier
{
    /// <summary>
    /// Sends a notification for a security alert to admin/superadmin users.
    /// </summary>
    /// <param name="alert">The security alert</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if notification was sent successfully</returns>
    Task<bool> NotifyAdminsAsync(SecurityAlert alert, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default no-op implementation of ISecurityAlertNotifier.
/// Used when no notification system is configured.
/// </summary>
public class NoOpSecurityAlertNotifier : ISecurityAlertNotifier
{
    public Task<bool> NotifyAdminsAsync(SecurityAlert alert, CancellationToken cancellationToken = default)
    {
        // No-op: Notifications disabled or not configured
        return Task.FromResult(false);
    }
}
