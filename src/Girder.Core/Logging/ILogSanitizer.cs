namespace Core.Common.Logging;

/// <summary>
/// Interface for sanitizing sensitive data from logs
/// </summary>
public interface ILogSanitizer
{
    /// <summary>
    /// Sanitizes sensitive data from an object before logging
    /// </summary>
    object? Sanitize(object? data);
    
    /// <summary>
    /// Checks if a property should be sanitized based on its name
    /// </summary>
    bool ShouldSanitizeProperty(string propertyName);
    
    /// <summary>
    /// Registers additional sensitive property names
    /// </summary>
    void RegisterSensitiveProperty(params string[] propertyNames);
}