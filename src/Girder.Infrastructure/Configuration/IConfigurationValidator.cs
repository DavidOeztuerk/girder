namespace Girder.Infrastructure.Configuration;

/// <summary>
/// Interface for configuration validation
/// </summary>
public interface IConfigurationValidator
{
    /// <summary>
    /// Validate configuration and return validation results
    /// </summary>
    ConfigurationValidationResult Validate();

    /// <summary>
    /// Get the configuration section name this validator handles
    /// </summary>
    string SectionName { get; }

    /// <summary>
    /// Get the priority of this validator (higher numbers run first)
    /// </summary>
    int Priority { get; }
}

/// <summary>
/// Result of configuration validation
/// </summary>
public class ConfigurationValidationResult
{
    /// <summary>
    /// Whether the configuration is valid
    /// </summary>
    public bool IsValid { get; set; } = true;

    /// <summary>
    /// Validation errors
    /// </summary>
    public List<ConfigurationValidationError> Errors { get; set; } = new();

    /// <summary>
    /// Validation warnings
    /// </summary>
    public List<ConfigurationValidationWarning> Warnings { get; set; } = new();

    /// <summary>
    /// Configuration section that was validated
    /// </summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>
    /// Add an error to the validation result
    /// </summary>
    public void AddError(string key, string message, string? suggestion = null)
    {
        Errors.Add(new ConfigurationValidationError
        {
            Key = key,
            Message = message,
            Suggestion = suggestion
        });
        IsValid = false;
    }

    /// <summary>
    /// Add a warning to the validation result
    /// </summary>
    public void AddWarning(string key, string message, string? suggestion = null)
    {
        Warnings.Add(new ConfigurationValidationWarning
        {
            Key = key,
            Message = message,
            Suggestion = suggestion
        });
    }

    /// <summary>
    /// Combine multiple validation results
    /// </summary>
    public static ConfigurationValidationResult Combine(params ConfigurationValidationResult[] results)
    {
        var combined = new ConfigurationValidationResult
        {
            IsValid = results.All(r => r.IsValid),
            SectionName = "Combined"
        };

        foreach (var result in results)
        {
            combined.Errors.AddRange(result.Errors);
            combined.Warnings.AddRange(result.Warnings);
        }

        return combined;
    }
}

/// <summary>
/// Configuration validation error
/// </summary>
public class ConfigurationValidationError
{
    /// <summary>
    /// Configuration key that has the error
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Error message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Suggested fix for the error
    /// </summary>
    public string? Suggestion { get; set; }

    /// <summary>
    /// Severity of the error
    /// </summary>
    public ErrorSeverity Severity { get; set; } = ErrorSeverity.Error;
}

/// <summary>
/// Configuration validation warning
/// </summary>
public class ConfigurationValidationWarning
{
    /// <summary>
    /// Configuration key that has the warning
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Warning message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Suggested improvement
    /// </summary>
    public string? Suggestion { get; set; }
}

/// <summary>
/// Error severity levels
/// </summary>
public enum ErrorSeverity
{
    Warning,
    Error,
    Critical
}

/// <summary>
/// Configuration validation options
/// </summary>
public class ConfigurationValidationOptions
{
    /// <summary>
    /// Whether to throw an exception on validation failure
    /// </summary>
    public bool ThrowOnValidationFailure { get; set; } = true;

    /// <summary>
    /// Whether to log validation results
    /// </summary>
    public bool LogValidationResults { get; set; } = true;

    /// <summary>
    /// Whether to validate configuration on startup
    /// </summary>
    public bool ValidateOnStartup { get; set; } = true;

    /// <summary>
    /// Whether to validate configuration when it changes
    /// </summary>
    public bool ValidateOnChange { get; set; } = false;

    /// <summary>
    /// Minimum severity level to include in validation
    /// </summary>
    public ErrorSeverity MinimumSeverity { get; set; } = ErrorSeverity.Warning;

    /// <summary>
    /// Whether to include warnings in validation failure
    /// </summary>
    public bool TreatWarningsAsErrors { get; set; } = false;
}