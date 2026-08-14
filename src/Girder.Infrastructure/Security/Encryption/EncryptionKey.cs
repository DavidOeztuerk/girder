using System.Text.Json.Serialization;

namespace Infrastructure.Security.Encryption;

/// <summary>
/// Encryption key representation
/// </summary>
public class EncryptionKey
{
    /// <summary>
    /// Unique key identifier
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Key type
    /// </summary>
    public KeyType KeyType { get; set; }

    /// <summary>
    /// Key purpose
    /// </summary>
    public KeyPurpose Purpose { get; set; }

    /// <summary>
    /// Key material (encrypted)
    /// </summary>
    [JsonIgnore]
    public byte[] KeyMaterial { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Key size in bits
    /// </summary>
    public int KeySize { get; set; }

    /// <summary>
    /// Key creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Key expiration timestamp
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Key status
    /// </summary>
    public KeyStatus Status { get; set; } = KeyStatus.Active;

    /// <summary>
    /// Key version (for rotation)
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Parent key ID (for derived keys)
    /// </summary>
    public string? ParentKeyId { get; set; }

    /// <summary>
    /// Key derivation info (for derived keys)
    /// </summary>
    public KeyDerivationInfo? DerivationInfo { get; set; }

    /// <summary>
    /// Usage restrictions
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

    /// <summary>
    /// Key metadata
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Usage statistics
    /// </summary>
    public KeyUsageStatistics UsageStatistics { get; set; } = new();

    /// <summary>
    /// Rotation schedule
    /// </summary>
    public KeyRotationSchedule? RotationSchedule { get; set; }

    /// <summary>
    /// Backup information
    /// </summary>
    public KeyBackupInfo? BackupInfo { get; set; }

    /// <summary>
    /// Check if key is valid for use
    /// </summary>
    public bool IsValid()
    {
        return Status == KeyStatus.Active &&
               (ExpiresAt == null || ExpiresAt > DateTime.UtcNow) &&
               KeyMaterial.Length > 0;
    }

    /// <summary>
    /// Check if key can be used for specific purpose
    /// </summary>
    public bool CanBeUsedFor(KeyPurpose purpose)
    {
        return IsValid() && (Purpose == purpose || Purpose == KeyPurpose.DataEncryption);
    }

    /// <summary>
    /// Check if key usage is within restrictions
    /// </summary>
    public bool IsUsageAllowed(string? userId = null, string? ipAddress = null, string? role = null)
    {
        if (!IsValid())
            return false;

        if (UsageRestrictions == null)
            return true;

        // Check operation limits
        if (UsageRestrictions.MaxEncryptionOperations.HasValue &&
            UsageStatistics.EncryptionOperations >= UsageRestrictions.MaxEncryptionOperations.Value)
        {
            return false;
        }

        // Check data size limits
        if (UsageRestrictions.MaxDataSize.HasValue &&
            UsageStatistics.TotalDataEncrypted >= UsageRestrictions.MaxDataSize.Value)
        {
            return false;
        }

        // Check IP restrictions
        if (UsageRestrictions.AllowedIpRanges.Any() && !string.IsNullOrEmpty(ipAddress))
        {
            if (!IsIpAllowed(ipAddress, UsageRestrictions.AllowedIpRanges))
                return false;
        }

        // Check role restrictions
        if (UsageRestrictions.AllowedRoles.Any() && !string.IsNullOrEmpty(role))
        {
            if (!UsageRestrictions.AllowedRoles.Contains(role))
                return false;
        }

        // Check time restrictions
        if (UsageRestrictions.TimeRestrictions != null)
        {
            if (!IsTimeAllowed(UsageRestrictions.TimeRestrictions))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Update usage statistics
    /// </summary>
    public void UpdateUsageStatistics(long dataSize = 0, bool isEncryption = true)
    {
        var now = DateTime.UtcNow;
        UsageStatistics.LastUsed = now;

        if (isEncryption)
        {
            UsageStatistics.EncryptionOperations++;
            UsageStatistics.TotalDataEncrypted += dataSize;
        }
        else
        {
            UsageStatistics.DecryptionOperations++;
            UsageStatistics.TotalDataDecrypted += dataSize;
        }

        // Update daily statistics
        var today = now.Date;
        if (!UsageStatistics.DailyUsage.ContainsKey(today))
        {
            UsageStatistics.DailyUsage[today] = new DailyUsageStatistics();
        }

        var dailyStats = UsageStatistics.DailyUsage[today];
        if (isEncryption)
        {
            dailyStats.EncryptionOperations++;
            dailyStats.DataEncrypted += dataSize;
        }
        else
        {
            dailyStats.DecryptionOperations++;
            dailyStats.DataDecrypted += dataSize;
        }

        // Clean up old daily statistics (keep last 90 days)
        var cutoff = now.AddDays(-90).Date;
        var keysToRemove = UsageStatistics.DailyUsage.Keys.Where(date => date < cutoff).ToList();
        foreach (var key in keysToRemove)
        {
            UsageStatistics.DailyUsage.Remove(key);
        }
    }

    private static bool IsIpAllowed(string ipAddress, List<string> allowedRanges)
    {
        // Simplified IP range checking - in production, use proper CIDR parsing
        foreach (var range in allowedRanges)
        {
            if (range == "*" || range == ipAddress)
                return true;

            if (range.Contains('/'))
            {
                // CIDR notation - implement proper CIDR checking
                // For now, simple prefix matching
                var prefix = range.Split('/')[0];
                if (ipAddress.StartsWith(prefix))
                    return true;
            }
        }

        return false;
    }

    private static bool IsTimeAllowed(TimeBasedRestrictions timeRestrictions)
    {
        var now = DateTime.UtcNow;

        // Check date range
        if (timeRestrictions.ValidFrom.HasValue && now < timeRestrictions.ValidFrom.Value)
            return false;

        if (timeRestrictions.ValidUntil.HasValue && now > timeRestrictions.ValidUntil.Value)
            return false;

        // Check day of week
        if (timeRestrictions.AllowedDaysOfWeek.Any() &&
            !timeRestrictions.AllowedDaysOfWeek.Contains(now.DayOfWeek))
            return false;

        // Check hour of day
        if (timeRestrictions.AllowedHoursOfDay.Any() &&
            !timeRestrictions.AllowedHoursOfDay.Contains(now.Hour))
            return false;

        return true;
    }
}

/// <summary>
/// Key metadata
/// </summary>
public class KeyMetadata
{
    /// <summary>
    /// Key identifier
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Key type
    /// </summary>
    public KeyType KeyType { get; set; }

    /// <summary>
    /// Key purpose
    /// </summary>
    public KeyPurpose Purpose { get; set; }

    /// <summary>
    /// Key size in bits
    /// </summary>
    public int KeySize { get; set; }

    /// <summary>
    /// Key status
    /// </summary>
    public KeyStatus Status { get; set; }

    /// <summary>
    /// Key version
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Expiration timestamp
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Last used timestamp
    /// </summary>
    public DateTime? LastUsed { get; set; }

    /// <summary>
    /// Usage count
    /// </summary>
    public long UsageCount { get; set; }

    /// <summary>
    /// Geographic restrictions
    /// </summary>
    public List<string> GeographicRestrictions { get; set; } = new();

    /// <summary>
    /// Compliance requirements
    /// </summary>
    public List<ComplianceRequirement> ComplianceRequirements { get; set; } = new();

    /// <summary>
    /// Next rotation date
    /// </summary>
    public DateTime? NextRotation { get; set; }

    /// <summary>
    /// Has backup
    /// </summary>
    public bool HasBackup { get; set; }
}

/// <summary>
/// Key derivation information
/// </summary>
public class KeyDerivationInfo
{
    /// <summary>
    /// Derivation function used
    /// </summary>
    public KeyDerivationFunction Function { get; set; }

    /// <summary>
    /// Salt used for derivation
    /// </summary>
    public byte[] Salt { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Iteration count
    /// </summary>
    public int Iterations { get; set; }

    /// <summary>
    /// Additional derivation parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// Key usage statistics
/// </summary>
public class KeyUsageStatistics
{
    /// <summary>
    /// Number of encryption operations
    /// </summary>
    public long EncryptionOperations { get; set; }

    /// <summary>
    /// Number of decryption operations
    /// </summary>
    public long DecryptionOperations { get; set; }

    /// <summary>
    /// Total data encrypted (bytes)
    /// </summary>
    public long TotalDataEncrypted { get; set; }

    /// <summary>
    /// Total data decrypted (bytes)
    /// </summary>
    public long TotalDataDecrypted { get; set; }

    /// <summary>
    /// Last used timestamp
    /// </summary>
    public DateTime? LastUsed { get; set; }

    /// <summary>
    /// First used timestamp
    /// </summary>
    public DateTime? FirstUsed { get; set; }

    /// <summary>
    /// Daily usage statistics
    /// </summary>
    public Dictionary<DateTime, DailyUsageStatistics> DailyUsage { get; set; } = new();

    /// <summary>
    /// Number of failed operations
    /// </summary>
    public long FailedOperations { get; set; }

    /// <summary>
    /// Peak operations per hour
    /// </summary>
    public long PeakOperationsPerHour { get; set; }

    /// <summary>
    /// Average operation duration (milliseconds)
    /// </summary>
    public double AverageOperationDuration { get; set; }
}

/// <summary>
/// Daily usage statistics
/// </summary>
public class DailyUsageStatistics
{
    /// <summary>
    /// Date
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Number of encryption operations
    /// </summary>
    public long EncryptionOperations { get; set; }

    /// <summary>
    /// Number of decryption operations
    /// </summary>
    public long DecryptionOperations { get; set; }

    /// <summary>
    /// Data encrypted (bytes)
    /// </summary>
    public long DataEncrypted { get; set; }

    /// <summary>
    /// Data decrypted (bytes)
    /// </summary>
    public long DataDecrypted { get; set; }

    /// <summary>
    /// Peak operations per hour
    /// </summary>
    public long PeakOperationsPerHour { get; set; }

    /// <summary>
    /// Unique users
    /// </summary>
    public HashSet<string> UniqueUsers { get; set; } = new();
}

/// <summary>
/// Key rotation schedule
/// </summary>
public class KeyRotationSchedule
{
    /// <summary>
    /// Rotation interval
    /// </summary>
    public TimeSpan Interval { get; set; }

    /// <summary>
    /// Next rotation date
    /// </summary>
    public DateTime NextRotation { get; set; }

    /// <summary>
    /// Auto-rotate enabled
    /// </summary>
    public bool AutoRotateEnabled { get; set; } = false;

    /// <summary>
    /// Warning threshold before rotation
    /// </summary>
    public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum key age before forced rotation
    /// </summary>
    public TimeSpan MaxKeyAge { get; set; } = TimeSpan.FromDays(365);

    /// <summary>
    /// Rotation based on usage count
    /// </summary>
    public long? RotationUsageThreshold { get; set; }

    /// <summary>
    /// Rotation based on data volume
    /// </summary>
    public long? RotationDataThreshold { get; set; }
}

/// <summary>
/// Key backup information
/// </summary>
public class KeyBackupInfo
{
    /// <summary>
    /// Backup ID
    /// </summary>
    public string BackupId { get; set; } = string.Empty;

    /// <summary>
    /// Backup timestamp
    /// </summary>
    public DateTime BackupTimestamp { get; set; }

    /// <summary>
    /// Backup location/storage
    /// </summary>
    public string BackupLocation { get; set; } = string.Empty;

    /// <summary>
    /// Backup verification hash
    /// </summary>
    public string VerificationHash { get; set; } = string.Empty;

    /// <summary>
    /// Backup encryption key ID
    /// </summary>
    public string? BackupEncryptionKeyId { get; set; }

    /// <summary>
    /// Recovery tested date
    /// </summary>
    public DateTime? RecoveryTestedDate { get; set; }

    /// <summary>
    /// Backup retention period
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(2555); // 7 years

    /// <summary>
    /// Backup status
    /// </summary>
    public BackupStatus Status { get; set; } = BackupStatus.Valid;
}

/// <summary>
/// Key status enumeration
/// </summary>
public enum KeyStatus
{
    /// <summary>
    /// Key is active and can be used
    /// </summary>
    Active,

    /// <summary>
    /// Key is disabled and cannot be used
    /// </summary>
    Disabled,

    /// <summary>
    /// Key is expired
    /// </summary>
    Expired,

    /// <summary>
    /// Key is scheduled for rotation
    /// </summary>
    PendingRotation,

    /// <summary>
    /// Key is being rotated
    /// </summary>
    Rotating,

    /// <summary>
    /// Key has been compromised
    /// </summary>
    Compromised,

    /// <summary>
    /// Key is archived (read-only)
    /// </summary>
    Archived,

    /// <summary>
    /// Key is destroyed
    /// </summary>
    Destroyed
}

/// <summary>
/// Backup status enumeration
/// </summary>
public enum BackupStatus
{
    /// <summary>
    /// Backup is valid
    /// </summary>
    Valid,

    /// <summary>
    /// Backup needs verification
    /// </summary>
    NeedsVerification,

    /// <summary>
    /// Backup verification failed
    /// </summary>
    VerificationFailed,

    /// <summary>
    /// Backup is corrupted
    /// </summary>
    Corrupted,

    /// <summary>
    /// Backup is expired
    /// </summary>
    Expired,

    /// <summary>
    /// Backup is missing
    /// </summary>
    Missing
}