using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Security.Audit;

/// <summary>
/// Extension methods for security audit services
/// </summary>
public static class SecurityAuditExtensions
{
  /// <summary>
  /// Add security audit services
  /// </summary>
  public static IServiceCollection AddSecurityAudit(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // Check environment variable as fallback
    var redisConnectionString = configuration.GetConnectionString("Redis")
        ?? configuration["Redis:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

    if (!string.IsNullOrEmpty(redisConnectionString))
    {
      // Redis-based audit service
      services.AddSingleton<ISecurityAuditService>(provider =>
      {
        var connectionMultiplexer = provider.GetRequiredService<IConnectionMultiplexer>();
        var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SecurityAuditService>>();

        // Get signing key from configuration or generate one
        var signingKeyBase64 = configuration["SecurityAudit:SigningKey"];
        byte[]? signingKey = null;
        if (!string.IsNullOrEmpty(signingKeyBase64))
        {
          try
          {
            signingKey = Convert.FromBase64String(signingKeyBase64);
          }
          catch
          {
            logger.LogWarning("Invalid signing key in configuration, generating new one");
          }
        }

        return new SecurityAuditService(connectionMultiplexer, logger, signingKey);
      });
    }
    else
    {
      // In-memory fallback
      services.AddSingleton<ISecurityAuditService, InMemorySecurityAuditService>();
    }

    // Add audit middleware
    services.AddTransient<SecurityAuditMiddleware>();

    // Add background services
    services.AddHostedService<SecurityAuditMaintenanceService>();

    // Configure audit options
    services.Configure<SecurityAuditOptions>(configuration.GetSection("SecurityAudit"));

    return services;
  }

  /// <summary>
  /// Add security audit middleware
  /// </summary>
  public static IServiceCollection AddSecurityAuditMiddleware(this IServiceCollection services)
  {
    services.AddTransient<SecurityAuditMiddleware>();
    return services;
  }
}

/// <summary>
/// Security audit configuration options
/// </summary>
public class SecurityAuditOptions
{
  /// <summary>
  /// Enable audit logging
  /// </summary>
  public bool EnableAuditLogging { get; set; } = true;

  /// <summary>
  /// Default retention period in days
  /// </summary>
  public int DefaultRetentionDays { get; set; } = 2555; // ~7 years

  /// <summary>
  /// Archive old logs after this many days
  /// </summary>
  public int ArchiveAfterDays { get; set; } = 365; // 1 year

  /// <summary>
  /// Maximum number of events to store
  /// </summary>
  public int MaxEvents { get; set; } = 1000000;

  /// <summary>
  /// Signing key for tamper detection (Base64 encoded)
  /// </summary>
  public string? SigningKey { get; set; }

  /// <summary>
  /// Enable integrity verification
  /// </summary>
  public bool EnableIntegrityVerification { get; set; } = true;

  /// <summary>
  /// Verify integrity every X hours
  /// </summary>
  public int IntegrityVerificationIntervalHours { get; set; } = 24;

  /// <summary>
  /// Log all requests or only sensitive ones
  /// </summary>
  public bool LogAllRequests { get; set; } = false;

  /// <summary>
  /// Include request/response bodies in logs
  /// </summary>
  public bool IncludeRequestBodies { get; set; } = true;

  /// <summary>
  /// Maximum request body size to log (bytes)
  /// </summary>
  public int MaxRequestBodySize { get; set; } = 1024 * 1024; // 1MB

  /// <summary>
  /// Compliance requirements
  /// </summary>
  public List<string> ComplianceRequirements { get; set; } = new() { "GDPR" };

  /// <summary>
  /// Export formats to support
  /// </summary>
  public List<string> SupportedExportFormats { get; set; } = new() { "JSON", "CSV", "XML" };
}

/// <summary>
/// Background service for audit maintenance tasks
/// </summary>
public class SecurityAuditMaintenanceService : BackgroundService
{
  private readonly ISecurityAuditService _auditService;
  private readonly Microsoft.Extensions.Logging.ILogger<SecurityAuditMaintenanceService> _logger;
  private readonly SecurityAuditOptions _options;

  public SecurityAuditMaintenanceService(
      ISecurityAuditService auditService,
      Microsoft.Extensions.Logging.ILogger<SecurityAuditMaintenanceService> logger,
      Microsoft.Extensions.Options.IOptions<SecurityAuditOptions> options)
  {
    _auditService = auditService;
    _logger = logger;
    _options = options.Value;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Security audit maintenance service started");

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PerformMaintenanceTasks(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during audit maintenance");
      }

      try
      {
        await Task.Delay(TimeSpan.FromHours(_options.IntegrityVerificationIntervalHours), stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }

    _logger.LogInformation("Security audit maintenance service stopped");
  }

  private async Task PerformMaintenanceTasks(CancellationToken cancellationToken)
  {
    _logger.LogDebug("Starting audit maintenance tasks");

    // Verify integrity
    if (_options.EnableIntegrityVerification)
    {
      await VerifyIntegrityAsync(cancellationToken);
    }

    // Archive old logs
    await ArchiveOldLogsAsync(cancellationToken);

    _logger.LogDebug("Audit maintenance tasks completed");
  }

  private async Task VerifyIntegrityAsync(CancellationToken cancellationToken)
  {
    try
    {
      _logger.LogDebug("Performing integrity verification");

      var result = await _auditService.VerifyAuditIntegrityAsync(
          DateTime.UtcNow.AddDays(-7), // Verify last 7 days
          DateTime.UtcNow,
          cancellationToken);

      if (!result.IsIntegrityIntact)
      {
        _logger.LogCritical(
            "Audit log integrity verification failed! {Violations} violations found in {Events} events",
            result.IntegrityViolations, result.EventsVerified);

        // Log integrity violation as audit event
        await _auditService.LogSecurityEventAsync(
            "AuditIntegrityViolation",
            $"Integrity verification failed with {result.IntegrityViolations} violations",
            SecurityEventSeverity.Critical,
            new { VerificationResult = result },
            cancellationToken);
      }
      else
      {
        _logger.LogInformation(
            "Audit log integrity verification passed for {Events} events",
            result.EventsVerified);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to verify audit integrity");
    }
  }

  private async Task ArchiveOldLogsAsync(CancellationToken cancellationToken)
  {
    try
    {
      var archiveDate = DateTime.UtcNow.AddDays(-_options.ArchiveAfterDays);
      var archivedCount = await _auditService.ArchiveOldLogsAsync(archiveDate, cancellationToken);

      if (archivedCount > 0)
      {
        _logger.LogInformation("Archived {Count} audit events older than {Date}",
            archivedCount, archiveDate);

        // Log archival as audit event
        await _auditService.LogSecurityEventAsync(
            "AuditLogsArchived",
            $"Archived {archivedCount} audit events older than {archiveDate:yyyy-MM-dd}",
            SecurityEventSeverity.Information,
            new { ArchivedCount = archivedCount, ArchiveDate = archiveDate },
            cancellationToken);
      }
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to archive old audit logs");
    }
  }
}

/// <summary>
/// In-memory security audit service for development/testing
/// </summary>
public class InMemorySecurityAuditService : ISecurityAuditService
{
  private readonly List<SecurityAuditEvent> _events = new();
  private readonly object _lock = new();
  private readonly Microsoft.Extensions.Logging.ILogger<InMemorySecurityAuditService> _logger;

  public InMemorySecurityAuditService(Microsoft.Extensions.Logging.ILogger<InMemorySecurityAuditService> logger)
  {
    _logger = logger;
  }

  public Task<string> LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      _events.Add(auditEvent);
      _logger.LogDebug("Security audit event logged (in-memory): {EventId} - {EventType}",
          auditEvent.Id, auditEvent.EventType);
    }
    return Task.FromResult(auditEvent.Id);
  }

  public Task<string> LogSecurityEventAsync(
      string eventType,
      string description,
      SecurityEventSeverity severity = SecurityEventSeverity.Information,
      object? additionalData = null,
      CancellationToken cancellationToken = default)
  {
    var auditEvent = new SecurityAuditEvent
    {
      EventType = eventType,
      Description = description,
      Severity = severity
    };

    return LogSecurityEventAsync(auditEvent, cancellationToken);
  }

  public Task<IEnumerable<SecurityAuditEvent>> GetSecurityEventsAsync(
      SecurityAuditQuery query,
      CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      var filtered = _events.AsEnumerable();

      if (query.FromDate.HasValue)
        filtered = filtered.Where(e => e.Timestamp >= query.FromDate.Value);

      if (query.ToDate.HasValue)
        filtered = filtered.Where(e => e.Timestamp <= query.ToDate.Value);

      if (!string.IsNullOrEmpty(query.UserId))
        filtered = filtered.Where(e => e.UserId == query.UserId);

      if (!string.IsNullOrEmpty(query.EventType))
        filtered = filtered.Where(e => e.EventType.Contains(query.EventType));

      if (query.Severity.HasValue)
        filtered = filtered.Where(e => e.Severity == query.Severity.Value);

      var result = filtered
          .OrderByDescending(e => e.Timestamp)
          .Skip((query.Page - 1) * query.PageSize)
          .Take(query.PageSize);

      return Task.FromResult(result);
    }
  }

  public Task<AuditIntegrityResult> VerifyAuditIntegrityAsync(
      DateTime? fromDate = null,
      DateTime? toDate = null,
      CancellationToken cancellationToken = default)
  {
    // Simplified integrity check for in-memory implementation
    var result = new AuditIntegrityResult
    {
      IsIntegrityIntact = true,
      EventsVerified = _events.Count,
      IntegrityViolations = 0
    };

    return Task.FromResult(result);
  }

  public Task<SecurityAuditReport> GenerateAuditReportAsync(
      SecurityAuditQuery query,
      CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      var events = _events.AsEnumerable();

      if (query.FromDate.HasValue)
        events = events.Where(e => e.Timestamp >= query.FromDate.Value);

      if (query.ToDate.HasValue)
        events = events.Where(e => e.Timestamp <= query.ToDate.Value);

      var eventList = events.ToList();

      var report = new SecurityAuditReport
      {
        PeriodStart = query.FromDate ?? DateTime.MinValue,
        PeriodEnd = query.ToDate ?? DateTime.MaxValue,
        TotalEvents = eventList.Count,
        EventsBySeverity = eventList.GroupBy(e => e.Severity).ToDictionary(g => g.Key, g => g.Count()),
        EventsByCategory = eventList.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Count())
      };

      return Task.FromResult(report);
    }
  }

  public Task<byte[]> ExportAuditLogsAsync(
      SecurityAuditQuery query,
      AuditExportFormat format = AuditExportFormat.Json,
      CancellationToken cancellationToken = default)
  {
    var events = GetSecurityEventsAsync(query, cancellationToken).Result;
    var json = System.Text.Json.JsonSerializer.Serialize(events);
    return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(json));
  }

  public Task<int> ArchiveOldLogsAsync(
      DateTime archiveBeforeDate,
      CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      var toArchive = _events.Where(e => e.Timestamp < archiveBeforeDate).ToList();
      foreach (var evt in toArchive)
      {
        _events.Remove(evt);
      }
      return Task.FromResult(toArchive.Count);
    }
  }

  public Task<SecurityAuditStatistics> GetAuditStatisticsAsync(
      DateTime? fromDate = null,
      DateTime? toDate = null,
      CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      var events = _events.AsEnumerable();

      if (fromDate.HasValue)
        events = events.Where(e => e.Timestamp >= fromDate.Value);

      if (toDate.HasValue)
        events = events.Where(e => e.Timestamp <= toDate.Value);

      var eventList = events.ToList();

      var statistics = new SecurityAuditStatistics
      {
        TotalEvents = eventList.Count,
        OldestEventTimestamp = eventList.Count > 0 ? eventList.Min(e => e.Timestamp) : null,
        NewestEventTimestamp = eventList.Count > 0 ? eventList.Max(e => e.Timestamp) : null
      };

      return Task.FromResult(statistics);
    }
  }
}