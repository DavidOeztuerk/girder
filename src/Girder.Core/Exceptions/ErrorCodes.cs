namespace Core.Common.Exceptions;

/// <summary>
/// Centralized error codes for the application
/// </summary>
public static class ErrorCodes
{
    // Domain Errors (1000-1999)
    public const string BusinessRuleViolation = "ERR_1000";
    public const string InvalidOperation = "ERR_1001";
    public const string ResourceNotFound = "ERR_1002";
    public const string ResourceAlreadyExists = "ERR_1003";
    public const string ConcurrencyConflict = "ERR_1004";
    public const string DataIntegrityViolation = "ERR_1005";
    
    // Authentication & Authorization (2000-2999)
    public const string Unauthorized = "ERR_2000";
    public const string InsufficientPermissions = "ERR_2001";
    public const string TokenExpired = "ERR_2002";
    public const string InvalidToken = "ERR_2003";
    public const string AccountLocked = "ERR_2004";
    public const string AccountNotVerified = "ERR_2005";
    public const string InvalidCredentials = "ERR_2006";
    public const string TwoFactorRequired = "ERR_2007";
    public const string InvalidTwoFactorCode = "ERR_2008";
    
    // Validation Errors (3000-3999)
    public const string ValidationFailed = "ERR_3000";
    public const string InvalidInput = "ERR_3001";
    public const string RequiredFieldMissing = "ERR_3002";
    public const string InvalidFormat = "ERR_3003";
    public const string OutOfRange = "ERR_3004";
    public const string InvalidLength = "ERR_3005";
    public const string InvalidEmail = "ERR_3006";
    public const string InvalidPhoneNumber = "ERR_3007";
    public const string InvalidUrl = "ERR_3008";
    
    // External Service Errors (4000-4999)
    public const string ExternalServiceError = "ERR_4000";
    public const string ServiceUnavailable = "ERR_4001";
    public const string ServiceTimeout = "ERR_4002";
    public const string RateLimitExceeded = "ERR_4003";
    public const string PaymentFailed = "ERR_4004";
    public const string EmailServiceError = "ERR_4005";
    public const string SmsServiceError = "ERR_4006";
    public const string StorageServiceError = "ERR_4007";
    public const string NotificationServiceError = "ERR_4008";
    
    // Database Errors (5000-5999)
    public const string DatabaseError = "ERR_5000";
    public const string DatabaseConnectionFailed = "ERR_5001";
    public const string DatabaseTimeout = "ERR_5002";
    public const string DuplicateKey = "ERR_5003";
    public const string ForeignKeyViolation = "ERR_5004";
    public const string DeadlockDetected = "ERR_5005";
    
    // File & Storage Errors (6000-6999)
    public const string FileNotFound = "ERR_6000";
    public const string FileUploadFailed = "ERR_6001";
    public const string FileTooLarge = "ERR_6002";
    public const string InvalidFileType = "ERR_6003";
    public const string StorageQuotaExceeded = "ERR_6004";
    public const string FileAccessDenied = "ERR_6005";
    
    // Business Logic Errors (7000-7999)
    public const string InsufficientBalance = "ERR_7000";
    public const string QuotaExceeded = "ERR_7001";
    public const string SubscriptionExpired = "ERR_7002";
    public const string FeatureNotAvailable = "ERR_7003";
    public const string MaxAttemptsExceeded = "ERR_7004";
    public const string DuplicateRequest = "ERR_7005";
    public const string InvalidState = "ERR_7006";
    public const string OperationNotAllowed = "ERR_7007";
    
    // System Errors (8000-8999)
    public const string InternalError = "ERR_8000";
    public const string ConfigurationError = "ERR_8001";
    public const string ConfigurationMissing = "ERR_8001_MISSING";
    public const string ConfigurationInvalid = "ERR_8001_INVALID";
    public const string StartupError = "ERR_8002";
    public const string ShutdownError = "ERR_8003";
    public const string MemoryError = "ERR_8004";
    public const string ThreadingError = "ERR_8005";
    
    // Network Errors (9000-9999)
    public const string NetworkError = "ERR_9000";
    public const string ConnectionTimeout = "ERR_9001";
    public const string ConnectionRefused = "ERR_9002";
    public const string DnsResolutionFailed = "ERR_9003";
    public const string SslError = "ERR_9004";
    public const string ProxyError = "ERR_9005";
}