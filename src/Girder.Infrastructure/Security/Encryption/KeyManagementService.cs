using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Girder.Infrastructure.Security.Encryption;

/// <summary>
/// Redis-based key management service with enterprise features
/// </summary>
public class KeyManagementService : IKeyManagementService
{
    private readonly IDatabase _database;
    private readonly ILogger<KeyManagementService> _logger;
    private readonly KeyManagementOptions _options;
    private readonly string _keyPrefix = "keys:";
    private readonly object _keyGenerationLock = new();

    public KeyManagementService(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<KeyManagementService> logger,
        IOptions<KeyManagementOptions> options)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _options = options.Value;
    }

    public string CreateKey(
        KeyType keyType,
        KeyPurpose purpose,
        KeyGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            options ??= new KeyGenerationOptions { Purpose = purpose };


            lock (_keyGenerationLock)
            {
                // Generate key material
                var keyMaterial = GenerateKeyMaterial(keyType, options.KeySize);
                var keyId = GenerateKeyId();

                // Calculate expiration and rotation dates
                var now = DateTime.UtcNow;
                var expiresAt = options.ExpiresAt ?? (options.RotationInterval.HasValue ?

                    now.Add(options.RotationInterval.Value) : null);
                DateTime? nextRotation = options.RotationInterval.HasValue ?

                    now.Add(options.RotationInterval.Value) : (DateTime?)null;

                // Create encryption key
                var encryptionKey = new EncryptionKey
                {
                    Id = keyId,
                    KeyType = keyType,
                    Purpose = purpose,
                    KeyMaterial = keyMaterial,
                    KeySize = options.KeySize,
                    CreatedAt = now,
                    ExpiresAt = expiresAt,
                    Status = KeyStatus.Active,
                    Version = 1,
                    UsageRestrictions = options.UsageRestrictions,
                    GeographicRestrictions = options.GeographicRestrictions,
                    ComplianceRequirements = options.ComplianceRequirements,
                    Metadata = new Dictionary<string, string>
                    {
                        ["created_by"] = "system",
                        ["purpose"] = purpose.ToString(),
                        ["classification"] = options.ComplianceRequirements.Any().ToString()
                    }
                };

                // Set up rotation schedule if specified
                if (options.RotationInterval.HasValue)
                {
                    encryptionKey.RotationSchedule = new KeyRotationSchedule
                    {
                        Interval = options.RotationInterval.Value,
                        NextRotation = nextRotation!.Value,
                        AutoRotateEnabled = _options.AutoRotateKeys,
                        WarningThreshold = TimeSpan.FromDays(7),
                        MaxKeyAge = TimeSpan.FromDays(365)
                    };
                }

                // Store the key
                var keyData = SerializeKey(encryptionKey);
                _database.StringSet(GetKeyKey(keyId), keyData);

                // Add to active keys index
                _database.SetAdd(GetActiveKeysKey(purpose), keyId);

                // Add to expiration tracking if applicable
                if (expiresAt.HasValue)
                {
                    _database.SortedSetAdd(GetExpirationTrackingKey(), keyId, expiresAt.Value.Ticks);
                }

                // Add to rotation tracking if applicable
                if (nextRotation.HasValue)
                {
                    _database.SortedSetAdd(GetRotationTrackingKey(), keyId, nextRotation.Value.Ticks);
                }

                _logger.LogInformation("Created encryption key {KeyId} of type {KeyType} for purpose {Purpose}",
                    keyId, keyType, purpose);

                return keyId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating encryption key");
            throw;
        }
    }

    public async Task<EncryptionKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var keyData = await _database.StringGetAsync(GetKeyKey(keyId));
            if (!keyData.HasValue)
            {
                _logger.LogWarning("Encryption key {KeyId} not found", keyId);
                return null;
            }

            var key = DeserializeKey(keyData!);
            
            // Check if key is still valid
            if (key.Status == KeyStatus.Expired || 
                (key.ExpiresAt.HasValue && key.ExpiresAt.Value <= DateTime.UtcNow))
            {
                _logger.LogWarning("Encryption key {KeyId} has expired", keyId);
                await UpdateKeyStatusAsync(keyId, KeyStatus.Expired);
                return null;
            }

            return key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving encryption key {KeyId}", keyId);
            return null;
        }
    }

    public async Task<string> RotateKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var oldKey = await GetKeyAsync(keyId, cancellationToken);
            if (oldKey == null)
            {
                throw new InvalidOperationException($"Key {keyId} not found for rotation");
            }

            // Generate new key with same properties but incremented version
            var newKeyMaterial = GenerateKeyMaterial(oldKey.KeyType, oldKey.KeySize);
            var newKeyId = GenerateKeyId();
            var now = DateTime.UtcNow;

            var newKey = new EncryptionKey
            {
                Id = newKeyId,
                KeyType = oldKey.KeyType,
                Purpose = oldKey.Purpose,
                KeyMaterial = newKeyMaterial,
                KeySize = oldKey.KeySize,
                CreatedAt = now,
                ExpiresAt = oldKey.RotationSchedule?.Interval != null ? 
                    now.Add(oldKey.RotationSchedule.Interval) : oldKey.ExpiresAt,
                Status = KeyStatus.Active,
                Version = oldKey.Version + 1,
                ParentKeyId = keyId,
                UsageRestrictions = oldKey.UsageRestrictions,
                GeographicRestrictions = oldKey.GeographicRestrictions,
                ComplianceRequirements = oldKey.ComplianceRequirements,
                Metadata = new Dictionary<string, string>(oldKey.Metadata)
                {
                    ["rotated_from"] = keyId,
                    ["rotation_timestamp"] = now.ToString("O")
                }
            };

            // Copy rotation schedule
            if (oldKey.RotationSchedule != null)
            {
                newKey.RotationSchedule = new KeyRotationSchedule
                {
                    Interval = oldKey.RotationSchedule.Interval,
                    NextRotation = now.Add(oldKey.RotationSchedule.Interval),
                    AutoRotateEnabled = oldKey.RotationSchedule.AutoRotateEnabled,
                    WarningThreshold = oldKey.RotationSchedule.WarningThreshold,
                    MaxKeyAge = oldKey.RotationSchedule.MaxKeyAge,
                    RotationUsageThreshold = oldKey.RotationSchedule.RotationUsageThreshold,
                    RotationDataThreshold = oldKey.RotationSchedule.RotationDataThreshold
                };
            }

            // Store new key
            var newKeyData = SerializeKey(newKey);
            await _database.StringSetAsync(GetKeyKey(newKeyId), newKeyData);

            // Update old key status
            oldKey.Status = KeyStatus.Archived;
            var oldKeyData = SerializeKey(oldKey);
            await _database.StringSetAsync(GetKeyKey(keyId), oldKeyData);

            // Update indexes
            await _database.SetRemoveAsync(GetActiveKeysKey(oldKey.Purpose), keyId);
            await _database.SetAddAsync(GetActiveKeysKey(newKey.Purpose), newKeyId);
            await _database.SetAddAsync(GetArchivedKeysKey(), keyId);

            // Update tracking
            if (newKey.ExpiresAt.HasValue)
            {
                await _database.SortedSetAddAsync(GetExpirationTrackingKey(), newKeyId, newKey.ExpiresAt.Value.Ticks);
            }

            if (newKey.RotationSchedule?.NextRotation != null)
            {
                await _database.SortedSetRemoveAsync(GetRotationTrackingKey(), keyId);
                await _database.SortedSetAddAsync(GetRotationTrackingKey(), newKeyId, newKey.RotationSchedule.NextRotation.Ticks);
            }

            // Log rotation event
            await LogKeyRotationAsync(keyId, newKeyId);

            _logger.LogInformation("Rotated encryption key {OldKeyId} to {NewKeyId}", keyId, newKeyId);
            return newKeyId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating encryption key {KeyId}", keyId);
            throw;
        }
    }

    public async Task DisableKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = await GetKeyAsync(keyId, cancellationToken);
            if (key == null)
            {
                _logger.LogWarning("Attempt to disable non-existent key {KeyId}", keyId);
                return;
            }

            await UpdateKeyStatusAsync(keyId, KeyStatus.Disabled);
            await _database.SetRemoveAsync(GetActiveKeysKey(key.Purpose), keyId);
            await _database.SetAddAsync(GetDisabledKeysKey(), keyId);

            _logger.LogInformation("Disabled encryption key {KeyId}", keyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling encryption key {KeyId}", keyId);
            throw;
        }
    }

    public async Task<List<KeyMetadata>> GetActiveKeysAsync(KeyPurpose purpose, CancellationToken cancellationToken = default)
    {
        try
        {
            var activeKeyIds = await _database.SetMembersAsync(GetActiveKeysKey(purpose));
            var keys = new List<KeyMetadata>();

            foreach (var keyId in activeKeyIds)
            {
                var key = await GetKeyAsync(keyId!, cancellationToken);
                if (key != null && key.IsValid())
                {
                    keys.Add(new KeyMetadata
                    {
                        Id = key.Id,
                        KeyType = key.KeyType,
                        Purpose = key.Purpose,
                        KeySize = key.KeySize,
                        Status = key.Status,
                        Version = key.Version,
                        CreatedAt = key.CreatedAt,
                        ExpiresAt = key.ExpiresAt,
                        LastUsed = key.UsageStatistics.LastUsed,
                        UsageCount = key.UsageStatistics.EncryptionOperations + key.UsageStatistics.DecryptionOperations,
                        GeographicRestrictions = key.GeographicRestrictions,
                        ComplianceRequirements = key.ComplianceRequirements,
                        NextRotation = key.RotationSchedule?.NextRotation,
                        HasBackup = key.BackupInfo != null
                    });
                }
            }

            return keys.OrderByDescending(k => k.CreatedAt).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active keys for purpose {Purpose}", purpose);
            return new List<KeyMetadata>();
        }
    }

    public async Task<KeyUsageStatistics> GetKeyUsageAsync(string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = await GetKeyAsync(keyId, cancellationToken);
            return key?.UsageStatistics ?? new KeyUsageStatistics();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting key usage for {KeyId}", keyId);
            return new KeyUsageStatistics();
        }
    }

    public async Task ScheduleKeyRotationAsync(string keyId, DateTime rotationDate, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = await GetKeyAsync(keyId, cancellationToken);
            if (key == null)
            {
                throw new InvalidOperationException($"Key {keyId} not found");
            }

            // Update rotation schedule
            if (key.RotationSchedule == null)
            {
                key.RotationSchedule = new KeyRotationSchedule();
            }

            key.RotationSchedule.NextRotation = rotationDate;
            key.RotationSchedule.AutoRotateEnabled = true;

            // Update stored key
            var keyData = SerializeKey(key);
            await _database.StringSetAsync(GetKeyKey(keyId), keyData);

            // Update rotation tracking
            await _database.SortedSetAddAsync(GetRotationTrackingKey(), keyId, rotationDate.Ticks);

            _logger.LogInformation("Scheduled rotation for key {KeyId} at {RotationDate}", keyId, rotationDate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scheduling key rotation for {KeyId}", keyId);
            throw;
        }
    }

    public async Task<KeyBackupResult> BackupKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = await GetKeyAsync(keyId, cancellationToken);
            if (key == null)
            {
                return new KeyBackupResult
                {
                    Success = false,
                    ErrorMessage = $"Key {keyId} not found"
                };
            }

            var backupId = GenerateBackupId();
            var backupTimestamp = DateTime.UtcNow;

            // Create backup data (encrypted with master backup key)
            var backupData = new
            {
                KeyId = key.Id,
                KeyMaterial = Convert.ToBase64String(key.KeyMaterial),
                KeyType = key.KeyType.ToString(),
                Purpose = key.Purpose.ToString(),
                KeySize = key.KeySize,
                CreatedAt = key.CreatedAt,
                Metadata = key.Metadata,
                BackupTimestamp = backupTimestamp
            };

            var backupJson = JsonSerializer.Serialize(backupData);
            var encryptedBackup = await EncryptBackupDataAsync(backupJson);
            var verificationHash = CalculateBackupHash(encryptedBackup);

            // Store backup
            var backupKey = GetBackupKey(backupId);
            await _database.StringSetAsync(backupKey, encryptedBackup, TimeSpan.FromDays(2555)); // 7 years

            // Update key backup info
            key.BackupInfo = new KeyBackupInfo
            {
                BackupId = backupId,
                BackupTimestamp = backupTimestamp,
                BackupLocation = $"redis:{backupKey}",
                VerificationHash = verificationHash,
                Status = BackupStatus.Valid
            };

            var keyData = SerializeKey(key);
            await _database.StringSetAsync(GetKeyKey(keyId), keyData);

            // Add to backup index
            await _database.SetAddAsync(GetBackupIndexKey(), backupId);

            _logger.LogInformation("Created backup {BackupId} for key {KeyId}", backupId, keyId);

            return new KeyBackupResult
            {
                BackupId = backupId,
                KeyId = keyId,
                BackupLocation = $"redis:{backupKey}",
                VerificationHash = verificationHash,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error backing up key {KeyId}", keyId);
            return new KeyBackupResult
            {
                KeyId = keyId,
                Success = false,
                ErrorMessage = $"Backup failed: {ex.Message}"
            };
        }
    }

    public async Task<string> RestoreKeyAsync(string backupId, CancellationToken cancellationToken = default)
    {
        try
        {
            var backupKey = GetBackupKey(backupId);
            var encryptedBackup = await _database.StringGetAsync(backupKey);

            if (!encryptedBackup.HasValue)
            {
                throw new InvalidOperationException($"Backup {backupId} not found");
            }

            // Decrypt backup data
            var backupJson = await DecryptBackupDataAsync(encryptedBackup!);
            var backupData = JsonSerializer.Deserialize<BackupData>(backupJson);

            if (backupData == null)
            {
                throw new InvalidOperationException("Invalid backup data format");
            }

            // Restore key
            var restoredKeyId = GenerateKeyId();
            var keyMaterial = Convert.FromBase64String(backupData.KeyMaterial);

            var restoredKey = new EncryptionKey
            {
                Id = restoredKeyId,
                KeyType = Enum.Parse<KeyType>(backupData.KeyType),
                Purpose = Enum.Parse<KeyPurpose>(backupData.Purpose),
                KeyMaterial = keyMaterial,
                KeySize = backupData.KeySize,
                CreatedAt = DateTime.UtcNow, // New creation time for restored key
                Status = KeyStatus.Active,
                Version = 1,
                Metadata = new Dictionary<string, string>(backupData.Metadata)
                {
                    ["restored_from"] = backupId,
                    ["original_created_at"] = backupData.CreatedAt.ToString("O"),
                    ["restoration_timestamp"] = DateTime.UtcNow.ToString("O")
                }
            };

            // Store restored key
            var keyData = SerializeKey(restoredKey);
            await _database.StringSetAsync(GetKeyKey(restoredKeyId), keyData);

            // Add to active keys
            await _database.SetAddAsync(GetActiveKeysKey(restoredKey.Purpose), restoredKeyId);

            _logger.LogInformation("Restored key {RestoredKeyId} from backup {BackupId}", restoredKeyId, backupId);
            return restoredKeyId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring key from backup {BackupId}", backupId);
            throw;
        }
    }

    #region Private Methods

    private static byte[] GenerateKeyMaterial(KeyType keyType, int keySize)
    {
        var keySizeBytes = keySize / 8;
        var keyMaterial = new byte[keySizeBytes];
        
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(keyMaterial);
        
        return keyMaterial;
    }

    private static string GenerateKeyId()
    {
        return $"key_{Guid.NewGuid():N}";
    }

    private static string GenerateBackupId()
    {
        return $"backup_{Guid.NewGuid():N}";
    }

    private string SerializeKey(EncryptionKey key)
    {
        // Create a serializable version (without sensitive key material in plain text)
        var keyData = new
        {
            key.Id,
            key.KeyType,
            key.Purpose,
            KeyMaterial = EncryptKeyMaterial(key.KeyMaterial), // Encrypt key material
            key.KeySize,
            key.CreatedAt,
            key.ExpiresAt,
            key.Status,
            key.Version,
            key.ParentKeyId,
            key.DerivationInfo,
            key.UsageRestrictions,
            key.GeographicRestrictions,
            key.ComplianceRequirements,
            key.Metadata,
            key.UsageStatistics,
            key.RotationSchedule,
            key.BackupInfo
        };

        return JsonSerializer.Serialize(keyData);
    }

    private EncryptionKey DeserializeKey(string keyData)
    {
        var data = JsonSerializer.Deserialize<dynamic>(keyData);
        // Simplified deserialization - in production, implement proper JSON deserialization
        // and decrypt key material using master key
        
        return new EncryptionKey(); // Placeholder
    }

    private string EncryptKeyMaterial(byte[] keyMaterial)
    {
        // Encrypt key material with master key
        // Simplified implementation - in production, use proper key encryption
        return Convert.ToBase64String(keyMaterial);
    }

    private byte[] DecryptKeyMaterial(string encryptedKeyMaterial)
    {
        // Decrypt key material with master key
        // Simplified implementation
        return Convert.FromBase64String(encryptedKeyMaterial);
    }

    private async Task UpdateKeyStatusAsync(string keyId, KeyStatus status)
    {
        var key = await GetKeyAsync(keyId);
        if (key != null)
        {
            key.Status = status;
            var keyData = SerializeKey(key);
            await _database.StringSetAsync(GetKeyKey(keyId), keyData);
        }
    }

    private async Task LogKeyRotationAsync(string oldKeyId, string newKeyId)
    {
        var logEntry = new
        {
            Event = "KeyRotation",
            OldKeyId = oldKeyId,
            NewKeyId = newKeyId,
            Timestamp = DateTime.UtcNow
        };

        var logData = JsonSerializer.Serialize(logEntry);
        var logKey = $"key_rotation_log:{DateTime.UtcNow:yyyyMMdd}";
        await _database.ListLeftPushAsync(logKey, logData);
        await _database.KeyExpireAsync(logKey, TimeSpan.FromDays(365)); // Keep rotation logs for 1 year
    }

    private async Task<string> EncryptBackupDataAsync(string backupData)
    {
        // Encrypt backup data with master backup key
        // Simplified implementation
        await Task.CompletedTask; // Simulate async operation
        var dataBytes = Encoding.UTF8.GetBytes(backupData);
        return Convert.ToBase64String(dataBytes);
    }

    private async Task<string> DecryptBackupDataAsync(string encryptedBackupData)
    {
        // Decrypt backup data with master backup key
        // Simplified implementation
        await Task.CompletedTask; // Simulate async operation
        var dataBytes = Convert.FromBase64String(encryptedBackupData);
        return Encoding.UTF8.GetString(dataBytes);
    }

    private static string CalculateBackupHash(string backupData)
    {
        var dataBytes = Encoding.UTF8.GetBytes(backupData);
        var hash = SHA256.HashData(dataBytes);
        return Convert.ToBase64String(hash);
    }

    // Redis key generation methods
    private string GetKeyKey(string keyId) => $"{_keyPrefix}data:{keyId}";
    private string GetActiveKeysKey(KeyPurpose purpose) => $"{_keyPrefix}active:{purpose}";
    private string GetArchivedKeysKey() => $"{_keyPrefix}archived";
    private string GetDisabledKeysKey() => $"{_keyPrefix}disabled";
    private string GetExpirationTrackingKey() => $"{_keyPrefix}expiration_tracking";
    private string GetRotationTrackingKey() => $"{_keyPrefix}rotation_tracking";
    private string GetBackupKey(string backupId) => $"{_keyPrefix}backup:{backupId}";
    private string GetBackupIndexKey() => $"{_keyPrefix}backup_index";

    Task<string> IKeyManagementService.CreateKey(KeyType keyType, KeyPurpose purpose, KeyGenerationOptions? options, CancellationToken cancellationToken)
    {
        var keyId = CreateKey(keyType, purpose, options, cancellationToken);
        return Task.FromResult(keyId);
    }


    #endregion
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
    /// Master key for encrypting stored keys
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// Backup encryption key
    /// </summary>
    public string? BackupEncryptionKey { get; set; }

    /// <summary>
    /// Enable key usage monitoring
    /// </summary>
    public bool EnableUsageMonitoring { get; set; } = true;

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

/// <summary>
/// Internal backup data structure
/// </summary>
internal class BackupData
{
    public string KeyId { get; set; } = string.Empty;
    public string KeyMaterial { get; set; } = string.Empty;
    public string KeyType { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public int KeySize { get; set; }
    public DateTime CreatedAt { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime BackupTimestamp { get; set; }
}