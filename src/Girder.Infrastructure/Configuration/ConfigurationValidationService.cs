using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Configuration;

/// <summary>
/// Service for validating application configuration
/// </summary>
public class ConfigurationValidationService : IConfigurationValidationService
{
    private readonly IEnumerable<IConfigurationValidator> _validators;
    private readonly ILogger<ConfigurationValidationService> _logger;
    private readonly ConfigurationValidationOptions _options;

    public ConfigurationValidationService(
        IEnumerable<IConfigurationValidator> validators,
        ILogger<ConfigurationValidationService> logger,
        IOptions<ConfigurationValidationOptions> options)
    {
        _validators = validators;
        _logger = logger;
        _options = options.Value;
    }

    public ConfigurationValidationResult ValidateAll()
    {
        _logger.LogInformation("Starting configuration validation with {ValidatorCount} validators", _validators.Count());

        var results = new List<ConfigurationValidationResult>();
        var validatorsByPriority = _validators.OrderByDescending(v => v.Priority);

        foreach (var validator in validatorsByPriority)
        {
            try
            {
                _logger.LogDebug("Validating configuration section: {SectionName}", validator.SectionName);
                
                var result = validator.Validate();
                result.SectionName = validator.SectionName;
                results.Add(result);

                if (_options.LogValidationResults)
                {
                    LogValidationResult(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Configuration validator for section '{SectionName}' threw an exception", validator.SectionName);
                
                var errorResult = new ConfigurationValidationResult
                {
                    SectionName = validator.SectionName,
                    IsValid = false
                };
                errorResult.AddError(validator.SectionName, $"Validator threw an exception: {ex.Message}", "Check the validator implementation");
                results.Add(errorResult);
            }
        }

        var combinedResult = ConfigurationValidationResult.Combine(results.ToArray());
        
        if (_options.LogValidationResults)
        {
            LogOverallResult(combinedResult);
        }

        if (!combinedResult.IsValid && _options.ThrowOnValidationFailure)
        {
            var errorMessage = $"Configuration validation failed with {combinedResult.Errors.Count} errors";
            throw new ConfigurationValidationException(errorMessage, combinedResult);
        }

        return combinedResult;
    }

    public ConfigurationValidationResult ValidateSection(string sectionName)
    {
        var validator = _validators.FirstOrDefault(v => v.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
        
        if (validator == null)
        {
            _logger.LogWarning("No validator found for configuration section: {SectionName}", sectionName);
            return new ConfigurationValidationResult
            {
                SectionName = sectionName,
                IsValid = true // No validator means no validation errors
            };
        }

        try
        {
            var result = validator.Validate();
            result.SectionName = sectionName;

            if (_options.LogValidationResults)
            {
                LogValidationResult(result);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Configuration validator for section '{SectionName}' threw an exception", sectionName);
            
            var errorResult = new ConfigurationValidationResult
            {
                SectionName = sectionName,
                IsValid = false
            };
            errorResult.AddError(sectionName, $"Validator threw an exception: {ex.Message}");
            return errorResult;
        }
    }

    private void LogValidationResult(ConfigurationValidationResult result)
    {
        if (result.IsValid && result.Warnings.Count == 0)
        {
            _logger.LogDebug("Configuration section '{SectionName}' is valid", result.SectionName);
            return;
        }

        if (result.Errors.Count > 0)
        {
            _logger.LogError("Configuration section '{SectionName}' has {ErrorCount} validation errors:", 
                result.SectionName, result.Errors.Count);
            
            foreach (var error in result.Errors)
            {
                _logger.LogError("  - {Key}: {Message} {Suggestion}", 
                    error.Key, error.Message, error.Suggestion != null ? $"(Suggestion: {error.Suggestion})" : "");
            }
        }

        if (result.Warnings.Count > 0)
        {
            _logger.LogWarning("Configuration section '{SectionName}' has {WarningCount} validation warnings:", 
                result.SectionName, result.Warnings.Count);
            
            foreach (var warning in result.Warnings)
            {
                _logger.LogWarning("  - {Key}: {Message} {Suggestion}", 
                    warning.Key, warning.Message, warning.Suggestion != null ? $"(Suggestion: {warning.Suggestion})" : "");
            }
        }
    }

    private void LogOverallResult(ConfigurationValidationResult result)
    {
        var totalErrors = result.Errors.Count;
        var totalWarnings = result.Warnings.Count;

        if (result.IsValid && totalWarnings == 0)
        {
            _logger.LogInformation("Configuration validation completed successfully - all sections are valid");
        }
        else if (result.IsValid && totalWarnings > 0)
        {
            _logger.LogWarning("Configuration validation completed with {WarningCount} warnings", totalWarnings);
        }
        else
        {
            _logger.LogError("Configuration validation failed with {ErrorCount} errors and {WarningCount} warnings", 
                totalErrors, totalWarnings);
        }
    }
}

/// <summary>
/// Interface for configuration validation service
/// </summary>
public interface IConfigurationValidationService
{
    /// <summary>
    /// Validate all registered configuration sections
    /// </summary>
    ConfigurationValidationResult ValidateAll();

    /// <summary>
    /// Validate a specific configuration section
    /// </summary>
    ConfigurationValidationResult ValidateSection(string sectionName);
}

/// <summary>
/// Exception thrown when configuration validation fails
/// </summary>
public class ConfigurationValidationException : Exception
{
    public ConfigurationValidationResult ValidationResult { get; }

    public ConfigurationValidationException(string message, ConfigurationValidationResult validationResult)
        : base(message)
    {
        ValidationResult = validationResult;
    }

    public ConfigurationValidationException(string message, ConfigurationValidationResult validationResult, Exception innerException)
        : base(message, innerException)
    {
        ValidationResult = validationResult;
    }
}