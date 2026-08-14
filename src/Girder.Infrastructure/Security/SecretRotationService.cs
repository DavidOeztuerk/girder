using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security;

/// <summary>
/// Background service for automatic secret rotation
/// </summary>
public class SecretRotationService : BackgroundService
{
    private readonly ISecretManager _secretManager;
    private readonly ILogger<SecretRotationService> _logger;
    private readonly SecretRotationOptions _options;

    public SecretRotationService(
        ISecretManager secretManager,
        ILogger<SecretRotationService> logger,
        IOptions<SecretRotationOptions> options)
    {
        _secretManager = secretManager;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableRotation)
        {
            _logger.LogInformation("Secret rotation is disabled");
            return;
        }

        _logger.LogInformation("Secret rotation service started with interval: {Interval} hours", 
            _options.RotationIntervalHours);

        var rotationInterval = TimeSpan.FromHours(_options.RotationIntervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformSecretRotation(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during secret rotation process");
            }

            try
            {
                await Task.Delay(rotationInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Secret rotation service stopped");
    }

    private async Task PerformSecretRotation(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting secret rotation process");

        var rotatedCount = 0;

        foreach (var secretName in _options.SecretsToRotate)
        {
            try
            {
                if (await ShouldRotateSecret(secretName))
                {
                    _logger.LogInformation("Rotating secret: {SecretName}", secretName);
                    
                    var newValue = await _secretManager.RotateSecretAsync(secretName, cancellationToken);
                    rotatedCount++;
                    
                    _logger.LogInformation("Successfully rotated secret: {SecretName}", secretName);
                    
                    // Clean up old versions
                    await CleanupOldSecretVersions(secretName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to rotate secret: {SecretName}", secretName);
            }
        }

        _logger.LogInformation("Secret rotation process completed. Rotated {Count} secrets", rotatedCount);
    }

    private async Task<bool> ShouldRotateSecret(string secretName)
    {
        try
        {
            var history = await _secretManager.GetSecretHistoryAsync(secretName);
            var latestVersion = history.FirstOrDefault(v => v.IsActive);

            if (latestVersion == null)
            {
                _logger.LogInformation("Secret {SecretName} not found, will create new one", secretName);
                return true;
            }

            var age = DateTime.UtcNow - latestVersion.CreatedAt;
            var shouldRotate = age.TotalHours >= _options.RotationIntervalHours;

            if (shouldRotate)
            {
                _logger.LogDebug("Secret {SecretName} is {Age} hours old, rotation needed", 
                    secretName, age.TotalHours);
            }

            return shouldRotate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if secret should be rotated: {SecretName}", secretName);
            return false;
        }
    }

    private async Task CleanupOldSecretVersions(string secretName)
    {
        try
        {
            var history = await _secretManager.GetSecretHistoryAsync(secretName);
            var versions = history.OrderByDescending(v => v.CreatedAt).ToList();

            if (versions.Count <= _options.KeepOldSecretsCount + 1) // +1 for active version
            {
                return;
            }

            _logger.LogDebug("Cleaning up old versions for secret: {SecretName}", secretName);

            // Keep active version + configured number of old versions
            var versionsToKeep = versions.Take(_options.KeepOldSecretsCount + 1).ToList();
            var versionsToDelete = versions.Skip(_options.KeepOldSecretsCount + 1);

            foreach (var versionToDelete in versionsToDelete)
            {
                // Note: This is a simplified cleanup. In a real implementation,
                // you might need more sophisticated cleanup logic depending on
                // how your secret storage is implemented.
                _logger.LogDebug("Would delete old version {Version} of secret {SecretName}", 
                    versionToDelete.Version, secretName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old secret versions: {SecretName}", secretName);
        }
    }
}

/// <summary>
/// Security audit logger interface
/// </summary>
public interface ISecurityAuditLogger
{
    /// <summary>
    /// Log a security event
    /// </summary>
    Task LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get security events
    /// </summary>
    Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(
        DateTime? fromDate = null, 
        DateTime? toDate = null,
        string? eventType = null,
        string? userId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Security audit event
/// </summary>
public class SecurityAuditEvent
{
    /// <summary>
    /// Event ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Event type
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
    /// IP address
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();

    /// <summary>
    /// Event timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Severity level
    /// </summary>
    public SecurityEventSeverity Severity { get; set; } = SecurityEventSeverity.Information;

    /// <summary>
    /// Source system
    /// </summary>
    public string Source { get; set; } = "Skillswap";
}

/// <summary>
/// Security event severity levels
/// </summary>
public enum SecurityEventSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Security audit logger implementation
/// </summary>
public class SecurityAuditLogger : ISecurityAuditLogger
{
    private readonly ILogger<SecurityAuditLogger> _logger;

    public SecurityAuditLogger(ILogger<SecurityAuditLogger> logger)
    {
        _logger = logger;
    }

    public Task LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        // Log to structured logging
        _logger.LogInformation(
            "Security Event: {EventType} - {Description} (User: {UserId}, Severity: {Severity})",
            auditEvent.EventType,
            auditEvent.Description,
            auditEvent.UserId,
            auditEvent.Severity);

        // In a real implementation, you would also store this in a secure audit log
        // that cannot be tampered with (e.g., write-only database, external audit service)

        return Task.CompletedTask;
    }

    public Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(
        DateTime? fromDate = null, 
        DateTime? toDate = null, 
        string? eventType = null, 
        string? userId = null, 
        CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would query the audit log storage
        _logger.LogInformation("Querying security events: From={FromDate}, To={ToDate}, Type={EventType}, User={UserId}",
            fromDate, toDate, eventType, userId);

        return Task.FromResult(Enumerable.Empty<SecurityAuditEvent>());
    }
}

