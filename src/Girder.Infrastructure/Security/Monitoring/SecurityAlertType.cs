namespace Infrastructure.Security.Monitoring;

/// <summary>
/// Types of security alerts
/// </summary>
public enum SecurityAlertType
{
    // Authentication & Authorization
    TokenTheftDetected,
    MultipleFailedLoginAttempts,
    SuspiciousLoginLocation,
    UnauthorizedAccessAttempt,

    // Session Management
    ConcurrentSessionLimitExceeded,
    SessionAnomalyDetected,
    SuspiciousDeviceRegistration,

    // Rate Limiting & DDoS
    RateLimitExceeded,
    PossibleDDoSAttack,
    BruteForceAttack,

    // Application Security
    CSPViolation,
    XSSAttempt,
    SQLInjectionAttempt,

    // E2EE & Cryptography
    E2EEKeyExchangeFailure,
    SignatureVerificationFailure,
    CryptographyError,

    // IP Reputation
    MaliciousIPDetected,
    IPReputationThreat,

    // User Behavior
    UnusualAPIUsage,
    SuspiciousDataAccess,
    MassDataExport,

    // System Health
    ServiceUnavailable,
    DatabaseConnectionFailure,
    RedisConnectionFailure,

    // Other
    Custom
}
