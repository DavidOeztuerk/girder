using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Configuration;

/// <summary>
/// Extension methods for configuration validation
/// </summary>
public static class ConfigurationValidationExtensions
{
    /// <summary>
    /// Add configuration validation services
    /// </summary>
    public static IServiceCollection AddConfigurationValidation(
        this IServiceCollection services,
        Action<ConfigurationValidationOptions>? configure = null)
    {
        // Configure options
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Register validation service
        services.AddSingleton<IConfigurationValidationService, ConfigurationValidationService>();

        // Register default validators
        services.AddSingleton<IConfigurationValidator, JwtConfigurationValidator>();
        services.AddSingleton<IConfigurationValidator, DatabaseConfigurationValidator>();
        services.AddSingleton<IConfigurationValidator, SmtpConfigurationValidator>();
        services.AddSingleton<IConfigurationValidator, RateLimitingConfigurationValidator>();

        // Add hosted service for startup validation
        services.AddHostedService<ConfigurationValidationHostedService>();

        return services;
    }

    /// <summary>
    /// Add a custom configuration validator
    /// </summary>
    public static IServiceCollection AddConfigurationValidator<T>(this IServiceCollection services)
        where T : class, IConfigurationValidator
    {
        services.AddSingleton<IConfigurationValidator, T>();
        return services;
    }

    /// <summary>
    /// Validate configuration immediately (useful for testing)
    /// </summary>
    public static void ValidateConfiguration(this IServiceProvider serviceProvider)
    {
        var validator = serviceProvider.GetRequiredService<IConfigurationValidationService>();
        validator.ValidateAll();
    }
}

/// <summary>
/// Hosted service that validates configuration on startup
/// </summary>
public class ConfigurationValidationHostedService : IHostedService
{
    private readonly IConfigurationValidationService _validationService;
    private readonly ILogger<ConfigurationValidationHostedService> _logger;

    public ConfigurationValidationHostedService(
        IConfigurationValidationService validationService,
        ILogger<ConfigurationValidationHostedService> logger)
    {
        _validationService = validationService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Validating application configuration on startup...");
            
            var result = _validationService.ValidateAll();
            
            if (result.IsValid)
            {
                _logger.LogInformation("Configuration validation completed successfully");
            }
            else
            {
                _logger.LogError("Configuration validation failed - application may not function correctly");
            }

            return Task.CompletedTask;
        }
        catch (ConfigurationValidationException ex)
        {
            _logger.LogCritical(ex, "Critical configuration errors detected - application cannot start");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during configuration validation");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Attribute to mark configuration classes for automatic validation
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ValidateConfigurationAttribute : Attribute
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public string SectionName { get; }

    /// <summary>
    /// Validation priority (higher runs first)
    /// </summary>
    public int Priority { get; set; } = 50;

    /// <summary>
    /// Whether this configuration is required
    /// </summary>
    public bool Required { get; set; } = true;

    public ValidateConfigurationAttribute(string sectionName)
    {
        SectionName = sectionName;
    }
}

/// <summary>
/// Attribute to mark properties that require validation
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RequiredConfigurationAttribute : Attribute
{
    /// <summary>
    /// Error message when the property is missing
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Suggestion for fixing the issue
    /// </summary>
    public string? Suggestion { get; set; }
}

/// <summary>
/// Attribute to mark properties with range validation
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class RangeConfigurationAttribute : Attribute
{
    /// <summary>
    /// Minimum value
    /// </summary>
    public object Minimum { get; }

    /// <summary>
    /// Maximum value
    /// </summary>
    public object Maximum { get; }

    /// <summary>
    /// Error message when the value is out of range
    /// </summary>
    public string? ErrorMessage { get; set; }

    public RangeConfigurationAttribute(object minimum, object maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }
}

/// <summary>
/// Generic configuration validator using attributes
/// </summary>
public class AttributeBasedConfigurationValidator<T> : IConfigurationValidator
    where T : class, new()
{
    private readonly IOptionsMonitor<T> _options;
    private readonly string _sectionName;
    private readonly int _priority;

    public AttributeBasedConfigurationValidator(IOptionsMonitor<T> options)
    {
        _options = options;
        
        var attribute = typeof(T).GetCustomAttributes(typeof(ValidateConfigurationAttribute), false)
            .Cast<ValidateConfigurationAttribute>()
            .FirstOrDefault();
        
        _sectionName = attribute?.SectionName ?? typeof(T).Name;
        _priority = attribute?.Priority ?? 50;
    }

    public string SectionName => _sectionName;
    public int Priority => _priority;

    public ConfigurationValidationResult Validate()
    {
        var result = new ConfigurationValidationResult { SectionName = SectionName };
        
        try
        {
            var config = _options.CurrentValue;
            var properties = typeof(T).GetProperties();

            foreach (var property in properties)
            {
                var value = property.GetValue(config);
                
                // Check required properties
                var requiredAttr = property.GetCustomAttributes(typeof(RequiredConfigurationAttribute), false)
                    .Cast<RequiredConfigurationAttribute>()
                    .FirstOrDefault();
                
                if (requiredAttr != null && (value == null || (value is string str && string.IsNullOrEmpty(str))))
                {
                    result.AddError(
                        $"{SectionName}:{property.Name}",
                        requiredAttr.ErrorMessage ?? $"{property.Name} is required",
                        requiredAttr.Suggestion);
                }

                // Check range validation
                var rangeAttr = property.GetCustomAttributes(typeof(RangeConfigurationAttribute), false)
                    .Cast<RangeConfigurationAttribute>()
                    .FirstOrDefault();
                
                if (rangeAttr != null && value != null)
                {
                    if (value is IComparable comparable)
                    {
                        if (comparable.CompareTo(rangeAttr.Minimum) < 0 || comparable.CompareTo(rangeAttr.Maximum) > 0)
                        {
                            result.AddError(
                                $"{SectionName}:{property.Name}",
                                rangeAttr.ErrorMessage ?? $"{property.Name} must be between {rangeAttr.Minimum} and {rangeAttr.Maximum}",
                                $"Set {property.Name} to a value between {rangeAttr.Minimum} and {rangeAttr.Maximum}");
                        }
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.AddError(SectionName, $"Failed to validate configuration: {ex.Message}");
            return result;
        }
    }
}