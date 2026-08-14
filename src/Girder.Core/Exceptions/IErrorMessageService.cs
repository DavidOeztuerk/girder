namespace Core.Common.Exceptions;

/// <summary>
/// Service for mapping error codes to user-friendly messages
/// </summary>
public interface IErrorMessageService
{
    /// <summary>
    /// Gets a user-friendly message for an error code
    /// </summary>
    string GetUserMessage(string errorCode, string? defaultMessage = null);
    
    /// <summary>
    /// Gets detailed help information for an error code
    /// </summary>
    string? GetHelpUrl(string errorCode);
    
    /// <summary>
    /// Gets suggested actions for resolving an error
    /// </summary>
    string[]? GetSuggestedActions(string errorCode);
    
    /// <summary>
    /// Checks if an error should be displayed to the user
    /// </summary>
    bool IsUserFacingError(string errorCode);
}