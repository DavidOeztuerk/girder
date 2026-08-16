using Girder.Abstractions.Security.Encryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Security.Encryption;

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
        services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();

        // Configure options
        services.Configure<DataEncryptionOptions>(configuration.GetSection("DataEncryption"));
        services.Configure<KeyManagementOptions>(configuration.GetSection("KeyManagement"));

        // Add background services

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
        services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();

        // Configure options
        services.Configure(configureEncryption);

        if (configureKeyManagement != null)
        {
            services.Configure(configureKeyManagement);
        }

        // Add background services

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
        _services.AddScoped<IFieldEncryptionService, FieldEncryptionService>();

        // Configure options
        _services.Configure<DataEncryptionOptions>(options => CopyOptions(_encryptionOptions, options));
        _services.Configure<KeyManagementOptions>(options => CopyOptions(_keyManagementOptions, options));

        // Add background services
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

