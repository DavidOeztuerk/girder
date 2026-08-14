using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security.Compliance;

/// <summary>
/// Extension methods for configuring GDPR compliance services
/// </summary>
public static class ComplianceExtensions
{
    /// <summary>
    /// Add GDPR and data protection compliance services
    /// </summary>
    public static IServiceCollection AddDataProtectionCompliance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register core compliance services
        // DataProtectionService implements IDataProtectionService, IConsentManagementService,
        // and IDataBreachNotificationService — register as all three
        services.AddSingleton<DataProtectionService>();
        services.AddSingleton<IDataProtectionService>(sp => sp.GetRequiredService<DataProtectionService>());
        services.AddSingleton<IConsentManagementService>(sp => sp.GetRequiredService<DataProtectionService>());
        services.AddSingleton<IDataBreachNotificationService>(sp => sp.GetRequiredService<DataProtectionService>());

        // Configure options
        services.Configure<DataProtectionOptions>(configuration.GetSection("DataProtection"));
        services.Configure<ConsentManagementOptions>(configuration.GetSection("ConsentManagement"));
        services.Configure<DataBreachOptions>(configuration.GetSection("DataBreach"));

        // DSGVO compliance background services
        services.AddHostedService<DataRetentionBackgroundService>();
        services.AddHostedService<ConsentMaintenanceBackgroundService>();
        services.AddHostedService<ComplianceMonitoringBackgroundService>();

        return services;
    }

    /// <summary>
    /// Add data protection compliance with custom configuration
    /// </summary>
    public static IServiceCollection AddDataProtectionCompliance(
        this IServiceCollection services,
        Action<DataProtectionOptions> configureDataProtection,
        Action<ConsentManagementOptions>? configureConsent = null,
        Action<DataBreachOptions>? configureBreach = null)
    {
        // Register services
        services.AddSingleton<DataProtectionService>();
        services.AddSingleton<IDataProtectionService>(sp => sp.GetRequiredService<DataProtectionService>());
        services.AddSingleton<IConsentManagementService>(sp => sp.GetRequiredService<DataProtectionService>());
        services.AddSingleton<IDataBreachNotificationService>(sp => sp.GetRequiredService<DataProtectionService>());

        // Configure options
        services.Configure(configureDataProtection);

        if (configureConsent != null)
        {
            services.Configure(configureConsent);
        }

        if (configureBreach != null)
        {
            services.Configure(configureBreach);
        }

        // DSGVO compliance background services
        services.AddHostedService<DataRetentionBackgroundService>();
        services.AddHostedService<ConsentMaintenanceBackgroundService>();
        services.AddHostedService<ComplianceMonitoringBackgroundService>();

        return services;
    }

    /// <summary>
    /// Add data protection compliance with fluent configuration
    /// </summary>
    public static IServiceCollection AddDataProtectionCompliance(
        this IServiceCollection services,
        Action<IComplianceBuilder> configure)
    {
        var builder = new ComplianceBuilder(services);
        configure(builder);

        return services;
    }
}

/// <summary>
/// Builder interface for configuring compliance services
/// </summary>
public interface IComplianceBuilder
{
    /// <summary>
    /// Configure data protection options
    /// </summary>
    IComplianceBuilder ConfigureDataProtection(Action<DataProtectionOptions> configure);

    /// <summary>
    /// Configure consent management options
    /// </summary>
    IComplianceBuilder ConfigureConsentManagement(Action<ConsentManagementOptions> configure);

    /// <summary>
    /// Configure data breach notification options
    /// </summary>
    IComplianceBuilder ConfigureDataBreachNotification(Action<DataBreachOptions> configure);

    /// <summary>
    /// Enable automated data retention
    /// </summary>
    IComplianceBuilder EnableAutomatedDataRetention(TimeSpan checkInterval);

    /// <summary>
    /// Enable consent monitoring
    /// </summary>
    IComplianceBuilder EnableConsentMonitoring(bool enabled = true);

    /// <summary>
    /// Configure data export settings
    /// </summary>
    IComplianceBuilder ConfigureDataExport(string exportDirectory, List<DataExportFormat> supportedFormats);

    /// <summary>
    /// Configure anonymization settings
    /// </summary>
    IComplianceBuilder ConfigureAnonymization(AnonymizationTechnique defaultTechnique, AnonymizationLevel defaultLevel);

    /// <summary>
    /// Configure pseudonymization settings
    /// </summary>
    IComplianceBuilder ConfigurePseudonymization(PseudonymizationTechnique defaultTechnique, bool defaultReversible);

    /// <summary>
    /// Add custom data source
    /// </summary>
    IComplianceBuilder AddDataSource(string name, Type serviceType);

    /// <summary>
    /// Configure for development environment
    /// </summary>
    IComplianceBuilder ForDevelopment();

    /// <summary>
    /// Configure for production environment
    /// </summary>
    IComplianceBuilder ForProduction();
}

/// <summary>
/// Implementation of compliance builder
/// </summary>
public class ComplianceBuilder : IComplianceBuilder
{
    private readonly IServiceCollection _services;
    private readonly DataProtectionOptions _dataProtectionOptions;
    private readonly ConsentManagementOptions _consentOptions;
    private readonly DataBreachOptions _breachOptions;

    public ComplianceBuilder(IServiceCollection services)
    {
        _services = services;
        _dataProtectionOptions = new DataProtectionOptions();
        _consentOptions = new ConsentManagementOptions();
        _breachOptions = new DataBreachOptions();

        // Register services
        _services.AddSingleton<DataProtectionService>();
        _services.AddSingleton<IDataProtectionService>(sp => sp.GetRequiredService<DataProtectionService>());
        _services.AddSingleton<IConsentManagementService>(sp => sp.GetRequiredService<DataProtectionService>());
        _services.AddSingleton<IDataBreachNotificationService>(sp => sp.GetRequiredService<DataProtectionService>());

        // Configure options
        _services.Configure<DataProtectionOptions>(options => CopyOptions(_dataProtectionOptions, options));
        _services.Configure<ConsentManagementOptions>(options => CopyOptions(_consentOptions, options));
        _services.Configure<DataBreachOptions>(options => CopyOptions(_breachOptions, options));

        // DSGVO compliance background services
        _services.AddHostedService<DataRetentionBackgroundService>();
        _services.AddHostedService<ConsentMaintenanceBackgroundService>();
        _services.AddHostedService<ComplianceMonitoringBackgroundService>();
    }

    public IComplianceBuilder ConfigureDataProtection(Action<DataProtectionOptions> configure)
    {
        configure(_dataProtectionOptions);
        return this;
    }

    public IComplianceBuilder ConfigureConsentManagement(Action<ConsentManagementOptions> configure)
    {
        configure(_consentOptions);
        return this;
    }

    public IComplianceBuilder ConfigureDataBreachNotification(Action<DataBreachOptions> configure)
    {
        configure(_breachOptions);
        return this;
    }

    public IComplianceBuilder EnableAutomatedDataRetention(TimeSpan checkInterval)
    {
        _dataProtectionOptions.EnableAutomatedDataRetention = true;
        _dataProtectionOptions.DataRetentionCheckInterval = checkInterval;
        return this;
    }

    public IComplianceBuilder EnableConsentMonitoring(bool enabled = true)
    {
        _consentOptions.EnableConsentMonitoring = enabled;
        return this;
    }

    public IComplianceBuilder ConfigureDataExport(string exportDirectory, List<DataExportFormat> supportedFormats)
    {
        _dataProtectionOptions.ExportDirectory = exportDirectory;
        _dataProtectionOptions.SupportedExportFormats = supportedFormats;
        return this;
    }

    public IComplianceBuilder ConfigureAnonymization(AnonymizationTechnique defaultTechnique, AnonymizationLevel defaultLevel)
    {
        _dataProtectionOptions.DefaultAnonymizationTechnique = defaultTechnique;
        _dataProtectionOptions.DefaultAnonymizationLevel = defaultLevel;
        _dataProtectionOptions.EnableAutomatedAnonymization = true;
        return this;
    }

    public IComplianceBuilder ConfigurePseudonymization(PseudonymizationTechnique defaultTechnique, bool defaultReversible)
    {
        _dataProtectionOptions.DefaultPseudonymizationTechnique = defaultTechnique;
        _dataProtectionOptions.DefaultPseudonymizationReversible = defaultReversible;
        _dataProtectionOptions.EnableAutomatedPseudonymization = true;
        return this;
    }

    public IComplianceBuilder AddDataSource(string name, Type serviceType)
    {
        _dataProtectionOptions.DataSources.Add(name, serviceType);
        return this;
    }

    public IComplianceBuilder ForDevelopment()
    {
        // Development-friendly settings
        _dataProtectionOptions.BypassIdentityVerification = true;
        _dataProtectionOptions.NotifyRecipientsOnRectification = false;
        _dataProtectionOptions.NotifyThirdPartiesOnErasure = false;
        _dataProtectionOptions.ExportDirectory = "./exports";
        _dataProtectionOptions.InlineDataThreshold = 1000;
        _dataProtectionOptions.DefaultRetentionPeriod = TimeSpan.FromDays(30); // Shorter for dev

        _consentOptions.RequireExplicitConsent = false; // Relaxed for dev
        _consentOptions.ConsentExpirationWarningDays = 7;
        _consentOptions.EnableConsentMonitoring = false;

        _breachOptions.AutoNotifyAuthority = false; // Don't auto-notify in dev
        _breachOptions.RequireManualApproval = true;
        _breachOptions.NotificationDelayMinutes = 60; // Longer delay for dev

        return this;
    }

    public IComplianceBuilder ForProduction()
    {
        // Production-grade settings
        _dataProtectionOptions.BypassIdentityVerification = false;
        _dataProtectionOptions.NotifyRecipientsOnRectification = true;
        _dataProtectionOptions.NotifyThirdPartiesOnErasure = true;
        _dataProtectionOptions.ExportDirectory = "/secure/exports";
        _dataProtectionOptions.InlineDataThreshold = 50;
        _dataProtectionOptions.DefaultRetentionPeriod = TimeSpan.FromDays(2555);
        _dataProtectionOptions.EnableAutomatedDataRetention = true;
        _dataProtectionOptions.DataRetentionCheckInterval = TimeSpan.FromHours(6);

        _consentOptions.RequireExplicitConsent = true;
        _consentOptions.ConsentExpirationWarningDays = 30;
        _consentOptions.EnableConsentMonitoring = true;
        _consentOptions.AutoWithdrawExpiredConsent = true;

        _breachOptions.AutoNotifyAuthority = true;
        _breachOptions.RequireManualApproval = false;
        _breachOptions.NotificationDelayMinutes = 5; // Quick notification in production
        _breachOptions.SupervisoryAuthorityDeadlineHours = 72; // GDPR requirement

        return this;
    }

    private static void CopyOptions<T>(T source, T destination)
    {
        var properties = typeof(T).GetProperties();
        foreach (var property in properties)
        {
            if (property.CanRead && property.CanWrite)
            {
                var value = property.GetValue(source);
                property.SetValue(destination, value);
            }
        }
    }
}

/// <summary>
/// Background service for automated data retention.
/// Calls IDataProtectionService.ApplyDataRetentionAsync which is currently
/// commented-out on the interface. This service will log and exit early
/// until that method is uncommented and implemented.
/// </summary>
public class DataRetentionBackgroundService : BackgroundService
{
    private readonly IDataProtectionService _dataProtectionService;
    private readonly ILogger<DataRetentionBackgroundService> _logger;
    private readonly DataProtectionOptions _options;

    public DataRetentionBackgroundService(
        IDataProtectionService dataProtectionService,
        ILogger<DataRetentionBackgroundService> logger,
        IOptions<DataProtectionOptions> options)
    {
        _dataProtectionService = dataProtectionService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableAutomatedDataRetention)
        {
            _logger.LogInformation("Automated data retention is disabled");
            return;
        }

        _logger.LogInformation("Data retention background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformDataRetentionCheckAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data retention check");
            }

            try
            {
                await Task.Delay(_options.DataRetentionCheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Data retention background service stopped");
    }

    private async Task PerformDataRetentionCheckAsync(CancellationToken cancellationToken)
    {
        var result = await _dataProtectionService.ApplyDataRetentionAsync(cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation(
                "[DataRetention] Completed: {Consents} consents expired, {Tokens} tokens cleaned, {Logs} logs archived",
                result.ExpiredConsentsMarked, result.ExpiredTokensRemoved, result.AuditLogsArchived);
        }
        else
        {
            _logger.LogWarning("[DataRetention] Completed with {ErrorCount} error(s): {Errors}",
                result.Errors.Count, string.Join("; ", result.Errors));
        }
    }
}

/// <summary>
/// Background service for consent maintenance
/// </summary>
public class ConsentMaintenanceBackgroundService : BackgroundService
{
    private readonly IConsentManagementService _consentService;
    private readonly IDataProtectionService _dataProtectionService;
    private readonly ILogger<ConsentMaintenanceBackgroundService> _logger;
    private readonly ConsentManagementOptions _options;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6);

    public ConsentMaintenanceBackgroundService(
        IConsentManagementService consentService,
        IDataProtectionService dataProtectionService,
        ILogger<ConsentMaintenanceBackgroundService> logger,
        IOptions<ConsentManagementOptions> options)
    {
        _consentService = consentService;
        _dataProtectionService = dataProtectionService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableConsentMonitoring)
        {
            _logger.LogInformation("Consent monitoring is disabled");
            return;
        }

        _logger.LogInformation("Consent maintenance background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformConsentMaintenanceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during consent maintenance");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Consent maintenance background service stopped");
    }

    private async Task PerformConsentMaintenanceAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("[ConsentMaintenance] Starting consent maintenance check");

        try
        {
            // Check all active consents for expiration via CheckConsentStatusAsync.
            // The DataProtectionService.ApplyDataRetentionAsync handles bulk marking,
            // but this service provides a second-pass validation per known data subjects.
            // For now, delegate the heavy lifting to the data protection service.
            var report = await _dataProtectionService.GenerateComplianceReportAsync(cancellationToken);

            if (report.ExpiredConsentsCount > 0)
            {
                _logger.LogInformation(
                    "[ConsentMaintenance] Found {Expired} expired consents during maintenance check",
                    report.ExpiredConsentsCount);
            }

            if (_options.AutoWithdrawExpiredConsent && report.ExpiredConsentsCount > 0)
            {
                _logger.LogWarning(
                    "[ConsentMaintenance] AutoWithdrawExpiredConsent is enabled — {Count} expired consents flagged for withdrawal",
                    report.ExpiredConsentsCount);
            }

            _logger.LogDebug("[ConsentMaintenance] Consent maintenance completed. Active: {Active}, Expired: {Expired}",
                report.ActiveConsentsCount, report.ExpiredConsentsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ConsentMaintenance] Error during consent maintenance");
        }
    }
}

/// <summary>
/// Background service for compliance monitoring.
/// Calls IDataProtectionService.GenerateComplianceReportAsync which is currently
/// commented-out on the interface. This service will log and exit early
/// until that method is uncommented and implemented.
/// </summary>
public class ComplianceMonitoringBackgroundService : BackgroundService
{
    private readonly IDataProtectionService _dataProtectionService;
    private readonly ILogger<ComplianceMonitoringBackgroundService> _logger;
    private readonly DataProtectionOptions _options;
    private readonly TimeSpan _monitoringInterval = TimeSpan.FromHours(24);

    public ComplianceMonitoringBackgroundService(
        IDataProtectionService dataProtectionService,
        ILogger<ComplianceMonitoringBackgroundService> logger,
        IOptions<DataProtectionOptions> options)
    {
        _dataProtectionService = dataProtectionService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableComplianceMonitoring)
        {
            _logger.LogInformation("Compliance monitoring is disabled");
            return;
        }

        _logger.LogInformation("Compliance monitoring background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformComplianceMonitoringAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during compliance monitoring");
            }

            try
            {
                await Task.Delay(_monitoringInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Compliance monitoring background service stopped");
    }

    private async Task PerformComplianceMonitoringAsync(CancellationToken cancellationToken)
    {
        var report = await _dataProtectionService.GenerateComplianceReportAsync(cancellationToken);

        _logger.LogInformation(
            "[ComplianceMonitor] Report: Active consents={Active}, Expired={Expired}, " +
            "Withdrawn={Withdrawn}, Open breaches={Breaches}",
            report.ActiveConsentsCount, report.ExpiredConsentsCount,
            report.WithdrawnConsentsCount, report.DataBreachesOpen);

        foreach (var warning in report.Warnings)
        {
            _logger.LogWarning("[ComplianceMonitor] {Warning}", warning);
        }
    }
}

/// <summary>
/// Configuration options for consent management
/// </summary>
public class ConsentManagementOptions
{
    /// <summary>
    /// Require explicit consent for all processing
    /// </summary>
    public bool RequireExplicitConsent { get; set; } = true;

    /// <summary>
    /// Enable consent monitoring
    /// </summary>
    public bool EnableConsentMonitoring { get; set; } = true;

    /// <summary>
    /// Auto-withdraw expired consent
    /// </summary>
    public bool AutoWithdrawExpiredConsent { get; set; } = false;

    /// <summary>
    /// Days before expiration to warn about consent
    /// </summary>
    public int ConsentExpirationWarningDays { get; set; } = 30;

    /// <summary>
    /// Maximum consent age before renewal required
    /// </summary>
    public TimeSpan MaxConsentAge { get; set; } = TimeSpan.FromDays(730);

    /// <summary>
    /// Consent verification method
    /// </summary>
    public ConsentVerificationMethod VerificationMethod { get; set; } = ConsentVerificationMethod.DoubleOptIn;
}

/// <summary>
/// Configuration options for data breach notification
/// </summary>
public class DataBreachOptions
{
    /// <summary>
    /// Automatically notify supervisory authority
    /// </summary>
    public bool AutoNotifyAuthority { get; set; } = false;

    /// <summary>
    /// Require manual approval for notifications
    /// </summary>
    public bool RequireManualApproval { get; set; } = true;

    /// <summary>
    /// Notification delay in minutes
    /// </summary>
    public int NotificationDelayMinutes { get; set; } = 30;

    /// <summary>
    /// Supervisory authority notification deadline (hours)
    /// </summary>
    public int SupervisoryAuthorityDeadlineHours { get; set; } = 72;

    /// <summary>
    /// Data subject notification threshold
    /// </summary>
    public int DataSubjectNotificationThreshold { get; set; } = 10;

    /// <summary>
    /// Breach severity for automatic authority notification
    /// </summary>
    public BreachSeverity AutoNotificationSeverityThreshold { get; set; } = BreachSeverity.High;
}

/// <summary>
/// Supporting enums and classes
/// </summary>
public enum ConsentVerificationMethod
{
    SingleOptIn,
    DoubleOptIn,
    ExplicitAction,
    DocumentedConsent
}

/// <summary>
/// Data retention request
/// </summary>
public class DataRetentionRequest
{
    public string RequestId { get; set; } = string.Empty;
    public DateTime CheckDate { get; set; } = DateTime.UtcNow;
    public bool AutomatedExecution { get; set; } = false;
    public List<string> DataTypes { get; set; } = new();
    public List<string> ProcessingPurposes { get; set; } = new();
}

/// <summary>
/// Compliance report request
/// </summary>
public class ComplianceReportRequest
{
    public ComplianceReportType ReportType { get; set; }
    public ReportPeriod Period { get; set; } = new();
    public bool IncludeRecommendations { get; set; } = true;
    public List<string> Filters { get; set; } = new();
}

/// <summary>
/// Consent withdrawal request
/// </summary>
public class ConsentWithdrawalRequest
{
    public string ConsentId { get; set; } = string.Empty;
    public string DataSubjectId { get; set; } = string.Empty;
    public string WithdrawalReason { get; set; } = string.Empty;
    public DateTime WithdrawalTimestamp { get; set; } = DateTime.UtcNow;
    public bool ProcessErasure { get; set; } = false;
}

/// <summary>
/// Extended data protection options
/// </summary>
public partial class DataProtectionOptions
{
    /// <summary>
    /// Enable automated data retention (DSGVO Art. 5(1)(e))
    /// </summary>
    public bool EnableAutomatedDataRetention { get; set; } = false;

    /// <summary>
    /// Data retention check interval
    /// </summary>
    public TimeSpan DataRetentionCheckInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Enable compliance monitoring background service (DSGVO Art. 30)
    /// </summary>
    public bool EnableComplianceMonitoring { get; set; } = false;

    /// <summary>
    /// Supported export formats
    /// </summary>
    public List<DataExportFormat> SupportedExportFormats { get; set; } = new() { DataExportFormat.JSON, DataExportFormat.CSV, DataExportFormat.PDF };

    /// <summary>
    /// Default anonymization technique
    /// </summary>
    public AnonymizationTechnique DefaultAnonymizationTechnique { get; set; } = AnonymizationTechnique.KAnonymity;

    /// <summary>
    /// Default anonymization level
    /// </summary>
    public AnonymizationLevel DefaultAnonymizationLevel { get; set; } = AnonymizationLevel.Medium;

    /// <summary>
    /// Default pseudonymization technique
    /// </summary>
    public PseudonymizationTechnique DefaultPseudonymizationTechnique { get; set; } = PseudonymizationTechnique.Tokenization;

    /// <summary>
    /// Default pseudonymization reversible setting
    /// </summary>
    public bool DefaultPseudonymizationReversible { get; set; } = true;

    /// <summary>
    /// Registered data sources
    /// </summary>
    public Dictionary<string, Type> DataSources { get; set; } = new();
}

/// <summary>
/// Compliance report type
/// </summary>
public enum ComplianceReportType
{
    DataSubjectRights,
    DataBreaches,
    ConsentManagement,
    DataRetention,
    OverallCompliance
}

/// <summary>
/// Report period
/// </summary>
public class ReportPeriod
{
    public DateTime StartDate { get; set; } = DateTime.UtcNow.Date.AddDays(-30);
    public DateTime EndDate { get; set; } = DateTime.UtcNow.Date;
}
