namespace Core.Common.Exceptions;

/// <summary>
/// Centralized help URLs for error documentation
/// </summary>
public static class HelpUrls
{
    private const string BaseUrl = "https://docs.skillswap.com/errors";
    
    // Authentication & Authorization
    public const string InvalidCredentials = $"{BaseUrl}/auth/invalid-credentials";
    public const string TokenExpired = $"{BaseUrl}/auth/token-expired";
    public const string UnauthorizedAccess = $"{BaseUrl}/auth/unauthorized";
    public const string InsufficientPermissions = $"{BaseUrl}/auth/insufficient-permissions";
    
    // Validation
    public const string RequiredFieldMissing = $"{BaseUrl}/validation/required-field";
    public const string InvalidFormat = $"{BaseUrl}/validation/invalid-format";
    public const string InvalidInput = $"{BaseUrl}/validation/invalid-input";
    public const string InvalidOperation = $"{BaseUrl}/validation/invalid-operation";
    
    // Resources
    public const string ResourceNotFound = $"{BaseUrl}/resources/not-found";
    public const string ResourceAlreadyExists = $"{BaseUrl}/resources/already-exists";
    
    // Business Rules
    public const string BusinessRuleViolation = $"{BaseUrl}/business/rule-violation";
    public const string QuotaExceeded = $"{BaseUrl}/business/quota-exceeded";
    public const string FeatureNotAvailable = $"{BaseUrl}/business/feature-unavailable";
    
    // System
    public const string InternalError = $"{BaseUrl}/system/internal-error";
    public const string ServiceUnavailable = $"{BaseUrl}/system/service-unavailable";
    public const string NetworkError = $"{BaseUrl}/system/network-error";
    public const string DatabaseError = $"{BaseUrl}/system/database-error";
    public const string ExternalServiceError = $"{BaseUrl}/system/external-service";
    
    // Rate Limiting
    public const string RateLimitExceeded = $"{BaseUrl}/limits/rate-limit";
    
    // Two-Factor Authentication
    public const string TwoFactorRequired = $"{BaseUrl}/auth/2fa-required";
    public const string InvalidTwoFactorCode = $"{BaseUrl}/auth/2fa-invalid";
    
    /// <summary>
    /// Get help URL for a specific error code
    /// </summary>
    public static string? GetHelpUrl(string? errorCode)
    {
        return errorCode switch
        {
            ErrorCodes.InvalidCredentials => InvalidCredentials,
            ErrorCodes.TokenExpired => TokenExpired,
            ErrorCodes.Unauthorized => UnauthorizedAccess,
            ErrorCodes.InsufficientPermissions => InsufficientPermissions,
            ErrorCodes.RequiredFieldMissing => RequiredFieldMissing,
            ErrorCodes.InvalidFormat => InvalidFormat,
            ErrorCodes.InvalidInput => InvalidInput,
            ErrorCodes.InvalidOperation => InvalidOperation,
            ErrorCodes.ResourceNotFound => ResourceNotFound,
            ErrorCodes.ResourceAlreadyExists => ResourceAlreadyExists,
            ErrorCodes.BusinessRuleViolation => BusinessRuleViolation,
            ErrorCodes.QuotaExceeded => QuotaExceeded,
            ErrorCodes.FeatureNotAvailable => FeatureNotAvailable,
            ErrorCodes.InternalError => InternalError,
            ErrorCodes.ServiceUnavailable => ServiceUnavailable,
            ErrorCodes.NetworkError => NetworkError,
            ErrorCodes.DatabaseError => DatabaseError,
            ErrorCodes.ExternalServiceError => ExternalServiceError,
            ErrorCodes.RateLimitExceeded => RateLimitExceeded,
            ErrorCodes.TwoFactorRequired => TwoFactorRequired,
            ErrorCodes.InvalidTwoFactorCode => InvalidTwoFactorCode,
            _ => null
        };
    }
}