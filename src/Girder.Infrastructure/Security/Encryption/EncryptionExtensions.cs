using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Security.Encryption;

/// <summary>
/// Extension methods for configuring encryption services
/// </summary>
public static class EncryptionExtensions
{
    /// <summary>
    /// Add data encryption services
    /// </summary>
    public static IServiceCollection AddDataEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register core encryption services
        services.AddSingleton<IKeyManagementService, KeyManagementService>();
        services.AddSingleton<IDataEncryptionService, DataEncryptionService>();
        services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();

        // Configure options
        services.Configure<DataEncryptionOptions>(configuration.GetSection("DataEncryption"));
        services.Configure<KeyManagementOptions>(configuration.GetSection("KeyManagement"));

        // Add background services
        services.AddHostedService<KeyRotationBackgroundService>();
        services.AddHostedService<KeyMaintenanceBackgroundService>();

        return services;
    }

    /// <summary>
    /// Add data encryption services with custom configuration
    /// </summary>
    public static IServiceCollection AddDataEncryption(
        this IServiceCollection services,
        Action<DataEncryptionOptions> configureEncryption,
        Action<KeyManagementOptions>? configureKeyManagement = null)
    {
        // Register services
        services.AddSingleton<IKeyManagementService, KeyManagementService>();
        services.AddSingleton<IDataEncryptionService, DataEncryptionService>();
        services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();

        // Configure options
        services.Configure(configureEncryption);

        if (configureKeyManagement != null)
        {
            services.Configure(configureKeyManagement);
        }

        // Add background services
        services.AddHostedService<KeyRotationBackgroundService>();
        services.AddHostedService<KeyMaintenanceBackgroundService>();

        return services;
    }

    /// <summary>
    /// Add data encryption with fluent configuration
    /// </summary>
    public static IServiceCollection AddDataEncryption(
        this IServiceCollection services,
        Action<IEncryptionBuilder> configure)
    {
        var builder = new EncryptionBuilder(services);
        configure(builder);

        return services;
    }
}

/// <summary>
/// Builder interface for configuring encryption services
/// </summary>
public interface IEncryptionBuilder
{
    /// <summary>
    /// Configure encryption options
    /// </summary>
    IEncryptionBuilder ConfigureEncryption(Action<DataEncryptionOptions> configure);

    /// <summary>
    /// Configure key management options
    /// </summary>
    IEncryptionBuilder ConfigureKeyManagement(Action<KeyManagementOptions> configure);

    /// <summary>
    /// Enable automatic key rotation
    /// </summary>
    IEncryptionBuilder EnableAutoKeyRotation(TimeSpan interval);

    /// <summary>
    /// Configure default encryption algorithm
    /// </summary>
    IEncryptionBuilder UseEncryptionAlgorithm(EncryptionAlgorithm algorithm);

    /// <summary>
    /// Configure default hashing algorithm
    /// </summary>
    IEncryptionBuilder UseHashingAlgorithm(HashingAlgorithm algorithm);

    /// <summary>
    /// Enable encryption operation logging
    /// </summary>
    IEncryptionBuilder EnableOperationLogging(bool enabled = true);

    /// <summary>
    /// Configure master encryption keys
    /// </summary>
    IEncryptionBuilder ConfigureMasterKeys(string encryptionKey, string? backupKey = null);

    /// <summary>
    /// Add custom key purpose
    /// </summary>
    IEncryptionBuilder AddKeyPurpose(KeyPurpose purpose, KeyGenerationOptions options);

    /// <summary>
    /// Configure for development environment
    /// </summary>
    IEncryptionBuilder ForDevelopment();

    /// <summary>
    /// Configure for production environment
    /// </summary>
    IEncryptionBuilder ForProduction();
}

/// <summary>
/// Implementation of encryption builder
/// </summary>
public class EncryptionBuilder : IEncryptionBuilder
{
    private readonly IServiceCollection _services;
    private readonly DataEncryptionOptions _encryptionOptions;
    private readonly KeyManagementOptions _keyManagementOptions;

    public EncryptionBuilder(IServiceCollection services)
    {
        _services = services;
        _encryptionOptions = new DataEncryptionOptions();
        _keyManagementOptions = new KeyManagementOptions();

        // Register services
        _services.AddSingleton<IKeyManagementService, KeyManagementService>();
        _services.AddSingleton<IDataEncryptionService, DataEncryptionService>();
        _services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();

        // Configure options
        _services.Configure<DataEncryptionOptions>(options => CopyOptions(_encryptionOptions, options));
        _services.Configure<KeyManagementOptions>(options => CopyOptions(_keyManagementOptions, options));

        // Add background services
        _services.AddHostedService<KeyRotationBackgroundService>();
        _services.AddHostedService<KeyMaintenanceBackgroundService>();
    }

    public IEncryptionBuilder ConfigureEncryption(Action<DataEncryptionOptions> configure)
    {
        configure(_encryptionOptions);
        return this;
    }

    public IEncryptionBuilder ConfigureKeyManagement(Action<KeyManagementOptions> configure)
    {
        configure(_keyManagementOptions);
        return this;
    }

    public IEncryptionBuilder EnableAutoKeyRotation(TimeSpan interval)
    {
        _keyManagementOptions.AutoRotateKeys = true;
        _keyManagementOptions.DefaultRotationInterval = interval;
        return this;
    }

    public IEncryptionBuilder UseEncryptionAlgorithm(EncryptionAlgorithm algorithm)
    {
        _encryptionOptions.DefaultAlgorithm = algorithm;
        return this;
    }

    public IEncryptionBuilder UseHashingAlgorithm(HashingAlgorithm algorithm)
    {
        _encryptionOptions.DefaultHashingAlgorithm = algorithm;
        return this;
    }

    public IEncryptionBuilder EnableOperationLogging(bool enabled = true)
    {
        _encryptionOptions.LogOperations = enabled;
        return this;
    }

    public IEncryptionBuilder ConfigureMasterKeys(string encryptionKey, string? backupKey = null)
    {
        _keyManagementOptions.MasterKey = encryptionKey;
        _keyManagementOptions.BackupEncryptionKey = backupKey;
        return this;
    }

    public IEncryptionBuilder AddKeyPurpose(KeyPurpose purpose, KeyGenerationOptions options)
    {
        // Store custom key purposes in configuration
        // This is a placeholder for custom key purpose configuration
        return this;
    }

    public IEncryptionBuilder ForDevelopment()
    {
        // Development-friendly settings
        _encryptionOptions.LogOperations = true;
        _encryptionOptions.CacheKeyMetadata = true;
        _encryptionOptions.MaxDataSize = 10 * 1024 * 1024; // 10 MB limit for dev

        _keyManagementOptions.AutoRotateKeys = false; // Don't auto-rotate in dev
        _keyManagementOptions.EnableUsageMonitoring = true;
        _keyManagementOptions.AutoCreateBackups = false; // Don't create backups in dev

        // Use weaker algorithms for faster development
        _encryptionOptions.DefaultAlgorithm = EncryptionAlgorithm.AES128GCM;
        _encryptionOptions.DefaultHashingAlgorithm = HashingAlgorithm.SHA256;

        return this;
    }

    public IEncryptionBuilder ForProduction()
    {
        // Production-grade settings
        _encryptionOptions.LogOperations = true;
        _encryptionOptions.CacheKeyMetadata = true;
        _encryptionOptions.MaxDataSize = 100 * 1024 * 1024; // 100 MB limit

        _keyManagementOptions.AutoRotateKeys = true;
        _keyManagementOptions.DefaultRotationInterval = TimeSpan.FromDays(90);
        _keyManagementOptions.EnableUsageMonitoring = true;
        _keyManagementOptions.AutoCreateBackups = true;
        _keyManagementOptions.MaxKeyVersions = 5; // Keep fewer versions in production

        // Use strong algorithms for production
        _encryptionOptions.DefaultAlgorithm = EncryptionAlgorithm.AES256GCM;
        _encryptionOptions.DefaultHashingAlgorithm = HashingAlgorithm.Argon2id;

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
/// Background service for automatic key rotation
/// </summary>
public class KeyRotationBackgroundService : BackgroundService
{
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<KeyRotationBackgroundService> _logger;
    private readonly KeyManagementOptions _options;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

    public KeyRotationBackgroundService(
        IKeyManagementService keyManagementService,
        ILogger<KeyRotationBackgroundService> logger,
        IOptions<KeyManagementOptions> options)
    {
        _keyManagementService = keyManagementService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.AutoRotateKeys)
        {
            _logger.LogInformation("Automatic key rotation is disabled");
            return;
        }

        _logger.LogInformation("Key rotation background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformKeyRotationCheckAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during key rotation check");
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

        _logger.LogInformation("Key rotation background service stopped");
    }

    private async Task PerformKeyRotationCheckAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Performing key rotation check");

        try
        {
            // Get all active keys that need rotation
            var purposes = Enum.GetValues<KeyPurpose>();

            foreach (var purpose in purposes)
            {
                var activeKeys = await _keyManagementService.GetActiveKeysAsync(purpose, cancellationToken);

                foreach (var keyMetadata in activeKeys)
                {
                    if (ShouldRotateKey(keyMetadata))
                    {
                        _logger.LogInformation("Rotating key {KeyId} due to schedule", keyMetadata.Id);

                        try
                        {
                            var newKeyId = await _keyManagementService.RotateKeyAsync(keyMetadata.Id, cancellationToken);
                            _logger.LogInformation("Successfully rotated key {OldKeyId} to {NewKeyId}",
                                keyMetadata.Id, newKeyId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to rotate key {KeyId}", keyMetadata.Id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during key rotation check");
        }
    }

    private bool ShouldRotateKey(KeyMetadata keyMetadata)
    {
        var now = DateTime.UtcNow;

        // Check if rotation is due
        if (keyMetadata.NextRotation.HasValue && keyMetadata.NextRotation.Value <= now)
        {
            return true;
        }

        // Check if key has expired
        if (keyMetadata.ExpiresAt.HasValue && keyMetadata.ExpiresAt.Value <= now)
        {
            return true;
        }

        // Check if key is too old (based on default rotation interval)
        var maxAge = _options.DefaultRotationInterval;
        if (now - keyMetadata.CreatedAt > maxAge)
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Background service for key maintenance tasks
/// </summary>
public class KeyMaintenanceBackgroundService : BackgroundService
{
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<KeyMaintenanceBackgroundService> _logger;
    private readonly KeyManagementOptions _options;
    private readonly TimeSpan _maintenanceInterval = TimeSpan.FromHours(6);

    public KeyMaintenanceBackgroundService(
        IKeyManagementService keyManagementService,
        ILogger<KeyMaintenanceBackgroundService> logger,
        IOptions<KeyManagementOptions> options)
    {
        _keyManagementService = keyManagementService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Key maintenance background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformMaintenanceTasks(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during key maintenance");
            }

            try
            {
                await Task.Delay(_maintenanceInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Key maintenance background service stopped");
    }

    private async Task PerformMaintenanceTasks(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Performing key maintenance tasks");

        try
        {
            // Clean up expired keys
            await CleanupExpiredKeysAsync(cancellationToken);

            // Create backups for keys that need them
            if (_options.AutoCreateBackups)
            {
                await CreateMissingBackupsAsync(cancellationToken);
            }

            // Monitor key usage patterns
            if (_options.EnableUsageMonitoring)
            {
                await MonitorKeyUsageAsync(cancellationToken);
            }

            // Verify backup integrity
            await VerifyBackupIntegrityAsync(cancellationToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during key maintenance tasks");
        }
    }

    private async Task CleanupExpiredKeysAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        try
        {
            // This would implement cleanup of keys that have been expired for longer than retention period
            _logger.LogDebug("Checking for expired keys to clean up");

            // Implementation would go here
            // Get all expired keys older than retention period
            // Securely delete them

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up expired keys");
        }
    }

    private async Task CreateMissingBackupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Checking for keys that need backups");

            var purposes = Enum.GetValues<KeyPurpose>();

            foreach (var purpose in purposes)
            {
                var activeKeys = await _keyManagementService.GetActiveKeysAsync(purpose, cancellationToken);

                foreach (var keyMetadata in activeKeys.Where(k => !k.HasBackup))
                {
                    try
                    {
                        _logger.LogInformation("Creating backup for key {KeyId}", keyMetadata.Id);
                        var backupResult = await _keyManagementService.BackupKeyAsync(keyMetadata.Id, cancellationToken);

                        if (backupResult.Success)
                        {
                            _logger.LogInformation("Successfully created backup {BackupId} for key {KeyId}",
                                backupResult.BackupId, keyMetadata.Id);
                        }
                        else
                        {
                            _logger.LogError("Failed to create backup for key {KeyId}: {Error}",
                                keyMetadata.Id, backupResult.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error creating backup for key {KeyId}", keyMetadata.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating missing backups");
        }
    }

    private async Task MonitorKeyUsageAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        try
        {
            _logger.LogDebug("Monitoring key usage patterns");

            // This would implement key usage monitoring
            // Check for unusual usage patterns
            // Alert on potential security issues
            // Generate usage reports

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring key usage");
        }
    }

    private async Task VerifyBackupIntegrityAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;

        try
        {
            _logger.LogDebug("Verifying backup integrity");

            // This would implement backup verification
            // Check backup hashes
            // Verify backup accessibility
            // Test restore procedures periodically

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying backup integrity");
        }
    }
}