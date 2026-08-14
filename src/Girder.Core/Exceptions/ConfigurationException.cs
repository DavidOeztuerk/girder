namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when there's a configuration error
/// </summary>
public class ConfigurationException : Exception
{
    public string ConfigurationKey { get; }
    public string? ConfigurationSection { get; }
    public string ErrorCode { get; }

    public ConfigurationException(
        string configurationKey,
        string? configurationSection = null,
        string? message = null)
        : base(message ?? $"Configuration error for key '{configurationKey}'" + 
               (configurationSection != null ? $" in section '{configurationSection}'" : ""))
    {
        ConfigurationKey = configurationKey;
        ConfigurationSection = configurationSection;
        ErrorCode = ErrorCodes.ConfigurationMissing;
    }

    public ConfigurationException(
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        ConfigurationKey = string.Empty;
        ErrorCode = ErrorCodes.ConfigurationError;
    }
    
    public int GetHttpStatusCode() => 500; // Internal Server Error for configuration issues
}