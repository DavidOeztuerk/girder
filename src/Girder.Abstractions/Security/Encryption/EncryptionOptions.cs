namespace Girder.Abstractions.Security.Encryption;

/// <summary>
/// Data encryption service configuration options
/// </summary>
public class DataEncryptionOptions
{
    /// <summary>
    /// Default encryption algorithm
    /// </summary>
    public EncryptionAlgorithm DefaultAlgorithm { get; set; } = EncryptionAlgorithm.AES256GCM;

    /// <summary>
    /// Default hashing algorithm
    /// </summary>
    public HashingAlgorithm DefaultHashingAlgorithm { get; set; } = HashingAlgorithm.Argon2id;

    /// <summary>
    /// Default pepper for hashing
    /// </summary>
    public string? DefaultPepper { get; set; }

    /// <summary>
    /// Log encryption/decryption operations
    /// </summary>
    public bool LogOperations { get; set; } = true;

    /// <summary>
    /// Cache key metadata
    /// </summary>
    public bool CacheKeyMetadata { get; set; } = true;

    /// <summary>
    /// Key metadata cache duration
    /// </summary>
    public TimeSpan KeyMetadataCacheDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Maximum data size for encryption (bytes)
    /// </summary>
    public long MaxDataSize { get; set; } = 100 * 1024 * 1024; // 100 MB

    /// <summary>
    /// Enable compression threshold (bytes)
    /// </summary>
    public long CompressionThreshold { get; set; } = 1024; // 1 KB
}

/// <summary>
/// Key management service configuration options
/// </summary>
public class KeyManagementOptions
{
    /// <summary>
    /// Enable automatic key rotation
    /// </summary>
    public bool AutoRotateKeys { get; set; } = false;

    /// <summary>
    /// Default key rotation interval
    /// </summary>
    public TimeSpan DefaultRotationInterval { get; set; } = TimeSpan.FromDays(90);


    /// <summary>
    /// Enable key usage monitoring
    /// </summary>
    public bool EnableUsageMonitoring { get; set; } = true;

    /// <summary>
    /// How often key maintenance runs — expiry cleanup, missing backups,
    /// usage monitoring and backup verification.
    /// </summary>
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Key retention period after expiration
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Maximum number of key versions to keep
    /// </summary>
    public int MaxKeyVersions { get; set; } = 10;

    /// <summary>
    /// Enable automatic backup creation
    /// </summary>
    public bool AutoCreateBackups { get; set; } = true;

    /// <summary>
    /// Backup retention period
    /// </summary>
    public TimeSpan BackupRetentionPeriod { get; set; } = TimeSpan.FromDays(2555); // 7 years
}
