namespace Infrastructure.Security.Encryption;

/// <summary>
/// Interface for data encryption and decryption services
/// </summary>
public interface IDataEncryptionService
{
    /// <summary>
    /// Encrypt sensitive data
    /// </summary>
    Task<EncryptionResult> EncryptAsync(string data, EncryptionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypt encrypted data
    /// </summary>
    Task<DecryptionResult> DecryptAsync(string encryptedData, EncryptionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypt data with specific key
    /// </summary>
    Task<EncryptionResult> EncryptWithKeyAsync(string data, string keyId, EncryptionOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypt data with specific key
    /// </summary>
    Task<DecryptionResult> DecryptWithKeyAsync(string encryptedData, string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hash sensitive data with salt
    /// </summary>
    Task<HashResult> HashAsync(string data, HashingOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify hashed data
    /// </summary>
    Task<bool> VerifyHashAsync(string data, string hashedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate encryption key
    /// </summary>
    Task<KeyGenerationResult> GenerateKeyAsync(KeyType keyType, KeyGenerationOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotate encryption key
    /// </summary>
    Task<KeyRotationResult> RotateKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get key metadata
    /// </summary>
    Task<KeyMetadata?> GetKeyMetadataAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-encrypt data with new key (for key rotation)
    /// </summary>
    Task<EncryptionResult> ReEncryptAsync(string encryptedData, string oldKeyId, string newKeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Secure delete of sensitive data
    /// </summary>
    Task SecureDeleteAsync(string keyId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for field-level encryption
/// </summary>
public interface IFieldEncryptionService
{
    /// <summary>
    /// Encrypt specific field in an object
    /// </summary>
    Task<T> EncryptFieldsAsync<T>(T obj, FieldEncryptionOptions? options = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Decrypt specific fields in an object
    /// </summary>
    Task<T> DecryptFieldsAsync<T>(T obj, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Encrypt JSON field
    /// </summary>
    Task<string> EncryptJsonFieldAsync(string json, string fieldPath, EncryptionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypt JSON field
    /// </summary>
    Task<string> DecryptJsonFieldAsync(string json, string fieldPath, EncryptionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get encryption status of fields
    /// </summary>
    Task<FieldEncryptionStatus> GetFieldEncryptionStatusAsync<T>(T obj, CancellationToken cancellationToken = default) where T : class;
}

/// <summary>
/// Interface for key management
/// </summary>
public interface IKeyManagementService
{
    /// <summary>
    /// Create new encryption key
    /// </summary>
    Task<string> CreateKey(KeyType keyType, KeyPurpose purpose, KeyGenerationOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get encryption key
    /// </summary>
    Task<EncryptionKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotate key (create new version)
    /// </summary>
    Task<string> RotateKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disable key
    /// </summary>
    Task DisableKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active keys for purpose
    /// </summary>
    Task<List<KeyMetadata>> GetActiveKeysAsync(KeyPurpose purpose, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get key usage statistics
    /// </summary>
    Task<KeyUsageStatistics> GetKeyUsageAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedule key rotation
    /// </summary>
    Task ScheduleKeyRotationAsync(string keyId, DateTime rotationDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backup key to secure storage
    /// </summary>
    Task<KeyBackupResult> BackupKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore key from backup
    /// </summary>
    Task<string> RestoreKeyAsync(string backupId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Encryption context for operations
/// </summary>
public class EncryptionContext
{
    /// <summary>
    /// Data classification level
    /// </summary>
    public DataClassification Classification { get; set; } = DataClassification.Internal;

    /// <summary>
    /// Purpose of encryption
    /// </summary>
    public EncryptionPurpose Purpose { get; set; } = EncryptionPurpose.Storage;

    /// <summary>
    /// User context for encryption
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// Organization context
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Compliance requirements
    /// </summary>
    public List<ComplianceRequirement> ComplianceRequirements { get; set; } = new();

    /// <summary>
    /// Data retention period
    /// </summary>
    public TimeSpan? RetentionPeriod { get; set; }

    /// <summary>
    /// Geographic restriction
    /// </summary>
    public string? GeographicRestriction { get; set; }
}

/// <summary>
/// Encryption options
/// </summary>
public class EncryptionOptions
{
    /// <summary>
    /// Encryption algorithm
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AES256GCM;

    /// <summary>
    /// Key derivation function
    /// </summary>
    public KeyDerivationFunction KeyDerivation { get; set; } = KeyDerivationFunction.PBKDF2;

    /// <summary>
    /// Number of iterations for key derivation
    /// </summary>
    public int Iterations { get; set; } = 100000;

    /// <summary>
    /// Include integrity check
    /// </summary>
    public bool IncludeIntegrityCheck { get; set; } = true;

    /// <summary>
    /// Compress before encryption
    /// </summary>
    public bool CompressBeforeEncryption { get; set; } = false;

    /// <summary>
    /// Additional authenticated data
    /// </summary>
    public byte[]? AdditionalData { get; set; }
}

/// <summary>
/// Hashing options
/// </summary>
public class HashingOptions
{
    /// <summary>
    /// Hashing algorithm
    /// </summary>
    public HashingAlgorithm Algorithm { get; set; } = HashingAlgorithm.Argon2id;

    /// <summary>
    /// Salt size in bytes
    /// </summary>
    public int SaltSize { get; set; } = 32;

    /// <summary>
    /// Hash size in bytes
    /// </summary>
    public int HashSize { get; set; } = 32;

    /// <summary>
    /// Memory cost (for Argon2)
    /// </summary>
    public int MemoryCost { get; set; } = 65536; // 64 MB

    /// <summary>
    /// Time cost (iterations)
    /// </summary>
    public int TimeCost { get; set; } = 3;

    /// <summary>
    /// Parallelism degree
    /// </summary>
    public int Parallelism { get; set; } = 1;

    /// <summary>
    /// Pepper (application-wide secret)
    /// </summary>
    public string? Pepper { get; set; }
}

/// <summary>
/// Field encryption options
/// </summary>
public class FieldEncryptionOptions
{
    /// <summary>
    /// Fields to encrypt (if null, auto-detect based on attributes)
    /// </summary>
    public List<string>? FieldsToEncrypt { get; set; }

    /// <summary>
    /// Fields to exclude from encryption
    /// </summary>
    public List<string> ExcludedFields { get; set; } = new();

    /// <summary>
    /// Encryption context
    /// </summary>
    public EncryptionContext Context { get; set; } = new();

    /// <summary>
    /// Preserve original field names
    /// </summary>
    public bool PreserveFieldNames { get; set; } = true;

    /// <summary>
    /// Include encryption metadata
    /// </summary>
    public bool IncludeMetadata { get; set; } = true;
}

/// <summary>
/// Key generation options
/// </summary>
public class KeyGenerationOptions
{
    /// <summary>
    /// Key purpose
    /// </summary>
    public KeyPurpose Purpose { get; set; } = KeyPurpose.DataEncryption;

    /// <summary>
    /// Key size in bits
    /// </summary>
    public int KeySize { get; set; } = 256;

    /// <summary>
    /// Key expiration
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Key rotation interval
    /// </summary>
    public TimeSpan? RotationInterval { get; set; }

    /// <summary>
    /// Key usage restrictions
    /// </summary>
    public KeyUsageRestrictions? UsageRestrictions { get; set; }

    /// <summary>
    /// Geographic restrictions
    /// </summary>
    public List<string> GeographicRestrictions { get; set; } = new();

    /// <summary>
    /// Compliance requirements
    /// </summary>
    public List<ComplianceRequirement> ComplianceRequirements { get; set; } = new();
}

/// <summary>
/// Encryption result
/// </summary>
public class EncryptionResult
{
    /// <summary>
    /// Encrypted data (Base64 encoded)
    /// </summary>
    public string EncryptedData { get; set; } = string.Empty;

    /// <summary>
    /// Key ID used for encryption
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Encryption algorithm used
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; }

    /// <summary>
    /// Initialization vector (Base64 encoded)
    /// </summary>
    public string? InitializationVector { get; set; }

    /// <summary>
    /// Authentication tag (Base64 encoded)
    /// </summary>
    public string? AuthenticationTag { get; set; }

    /// <summary>
    /// Encryption timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Data integrity hash
    /// </summary>
    public string? IntegrityHash { get; set; }

    /// <summary>
    /// Encryption metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Decryption result
/// </summary>
public class DecryptionResult
{
    /// <summary>
    /// Decrypted data
    /// </summary>
    public string Data { get; set; } = string.Empty;

    /// <summary>
    /// Key ID used for decryption
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Decryption timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Original encryption timestamp
    /// </summary>
    public DateTime? OriginalTimestamp { get; set; }

    /// <summary>
    /// Data integrity verified
    /// </summary>
    public bool IntegrityVerified { get; set; } = true;

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Warnings
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Hash result
/// </summary>
public class HashResult
{
    /// <summary>
    /// Hashed data (Base64 encoded)
    /// </summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// Salt used (Base64 encoded)
    /// </summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>
    /// Hashing algorithm used
    /// </summary>
    public HashingAlgorithm Algorithm { get; set; }

    /// <summary>
    /// Hash parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Hash timestamp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Key generation result
/// </summary>
public class KeyGenerationResult
{
    /// <summary>
    /// Generated key ID
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Key type
    /// </summary>
    public KeyType KeyType { get; set; }

    /// <summary>
    /// Key purpose
    /// </summary>
    public KeyPurpose Purpose { get; set; }

    /// <summary>
    /// Key creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Key expiration
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Key rotation schedule
    /// </summary>
    public DateTime? NextRotation { get; set; }

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Key rotation result
/// </summary>
public class KeyRotationResult
{
    /// <summary>
    /// Old key ID
    /// </summary>
    public string OldKeyId { get; set; } = string.Empty;

    /// <summary>
    /// New key ID
    /// </summary>
    public string NewKeyId { get; set; } = string.Empty;

    /// <summary>
    /// Rotation timestamp
    /// </summary>
    public DateTime RotationTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Next scheduled rotation
    /// </summary>
    public DateTime? NextRotation { get; set; }

    /// <summary>
    /// Data re-encryption required
    /// </summary>
    public bool ReEncryptionRequired { get; set; } = true;

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Key backup result
/// </summary>
public class KeyBackupResult
{
    /// <summary>
    /// Backup ID
    /// </summary>
    public string BackupId { get; set; } = string.Empty;

    /// <summary>
    /// Key ID that was backed up
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Backup timestamp
    /// </summary>
    public DateTime BackupTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Backup location
    /// </summary>
    public string? BackupLocation { get; set; }

    /// <summary>
    /// Backup verification hash
    /// </summary>
    public string? VerificationHash { get; set; }

    /// <summary>
    /// Success indicator
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Field encryption status
/// </summary>
public class FieldEncryptionStatus
{
    /// <summary>
    /// Encrypted fields
    /// </summary>
    public Dictionary<string, FieldEncryptionInfo> EncryptedFields { get; set; } = new();

    /// <summary>
    /// Unencrypted sensitive fields
    /// </summary>
    public List<string> UnencryptedSensitiveFields { get; set; } = new();

    /// <summary>
    /// Overall encryption coverage percentage
    /// </summary>
    public double EncryptionCoverage { get; set; }

    /// <summary>
    /// Compliance status
    /// </summary>
    public bool IsCompliant { get; set; } = true;

    /// <summary>
    /// Compliance violations
    /// </summary>
    public List<string> ComplianceViolations { get; set; } = new();
}

/// <summary>
/// Field encryption info
/// </summary>
public class FieldEncryptionInfo
{
    /// <summary>
    /// Field name
    /// </summary>
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    /// Encryption key ID
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Encryption algorithm
    /// </summary>
    public EncryptionAlgorithm Algorithm { get; set; }

    /// <summary>
    /// Encryption timestamp
    /// </summary>
    public DateTime EncryptionTimestamp { get; set; }

    /// <summary>
    /// Data classification
    /// </summary>
    public DataClassification Classification { get; set; }
}

/// <summary>
/// Data classification levels
/// </summary>
public enum DataClassification
{
    Public,
    Internal,
    Confidential,
    Restricted,
    TopSecret
}

/// <summary>
/// Encryption purposes
/// </summary>
public enum EncryptionPurpose
{
    Storage,
    Transit,
    Backup,
    Archive,
    Processing,
    Sharing
}

/// <summary>
/// Encryption algorithms
/// </summary>
public enum EncryptionAlgorithm
{
    AES128GCM,
    AES256GCM,
    AES128CBC,
    AES256CBC,
    ChaCha20Poly1305,
    XChaCha20Poly1305
}

/// <summary>
/// Hashing algorithms
/// </summary>
public enum HashingAlgorithm
{
    SHA256,
    SHA512,
    Argon2id,
    Argon2i,
    Argon2d,
    BCrypt,
    SCrypt,
    PBKDF2
}

/// <summary>
/// Key derivation functions
/// </summary>
public enum KeyDerivationFunction
{
    PBKDF2,
    Argon2id,
    SCrypt,
    HKDF
}

/// <summary>
/// Key types
/// </summary>
public enum KeyType
{
    Symmetric,
    Asymmetric,
    Hybrid
}

/// <summary>
/// Key purposes
/// </summary>
public enum KeyPurpose
{
    DataEncryption,
    KeyEncryption,
    Signing,
    Authentication,
    DerivedKey
}

/// <summary>
/// Compliance requirements
/// </summary>
public enum ComplianceRequirement
{
    GDPR,
    HIPAA,
    PCI_DSS,
    SOX,
    FIPS_140_2,
    Common_Criteria,
    ISO_27001
}

/// <summary>
/// Key usage restrictions
/// </summary>
public class KeyUsageRestrictions
{
    /// <summary>
    /// Maximum number of encryption operations
    /// </summary>
    public long? MaxEncryptionOperations { get; set; }

    /// <summary>
    /// Maximum data size that can be encrypted
    /// </summary>
    public long? MaxDataSize { get; set; }

    /// <summary>
    /// Allowed IP ranges
    /// </summary>
    public List<string> AllowedIpRanges { get; set; } = new();

    /// <summary>
    /// Allowed user roles
    /// </summary>
    public List<string> AllowedRoles { get; set; } = new();

    /// <summary>
    /// Time-based restrictions
    /// </summary>
    public TimeBasedRestrictions? TimeRestrictions { get; set; }
}

/// <summary>
/// Time-based key usage restrictions
/// </summary>
public class TimeBasedRestrictions
{
    /// <summary>
    /// Valid from time
    /// </summary>
    public DateTime? ValidFrom { get; set; }

    /// <summary>
    /// Valid until time
    /// </summary>
    public DateTime? ValidUntil { get; set; }

    /// <summary>
    /// Allowed days of week
    /// </summary>
    public List<DayOfWeek> AllowedDaysOfWeek { get; set; } = new();

    /// <summary>
    /// Allowed hours of day (24-hour format)
    /// </summary>
    public List<int> AllowedHoursOfDay { get; set; } = new();
}