using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Security.Encryption;

/// <summary>
/// Advanced data encryption service with key management
/// </summary>
public class DataEncryptionService : IDataEncryptionService
{
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<DataEncryptionService> _logger;
    private readonly DataEncryptionOptions _options;
    private readonly IDatabase _database;

    public DataEncryptionService(
        IKeyManagementService keyManagementService,
        ILogger<DataEncryptionService> logger,
        IOptions<DataEncryptionOptions> options,
        IConnectionMultiplexer connectionMultiplexer)
    {
        _keyManagementService = keyManagementService;
        _logger = logger;
        _options = options.Value;
        _database = connectionMultiplexer.GetDatabase();
    }

    public async Task<EncryptionResult> EncryptAsync(
        string data,
        EncryptionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(data))
            {
                return new EncryptionResult
                {
                    Success = false,
                    ErrorMessage = "Data cannot be null or empty"
                };
            }

            // Select appropriate key based on context
            var keyId = await SelectEncryptionKeyAsync(context, cancellationToken);
            if (string.IsNullOrEmpty(keyId))
            {
                return new EncryptionResult
                {
                    Success = false,
                    ErrorMessage = "No suitable encryption key found"
                };
            }

            var encryptionOptions = CreateEncryptionOptions(context);
            return await EncryptWithKeyAsync(data, keyId, encryptionOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data");
            return new EncryptionResult
            {
                Success = false,
                ErrorMessage = $"Encryption failed: {ex.Message}"
            };
        }
    }

    public async Task<DecryptionResult> DecryptAsync(
        string encryptedData,
        EncryptionContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(encryptedData))
            {
                return new DecryptionResult
                {
                    Success = false,
                    ErrorMessage = "Encrypted data cannot be null or empty"
                };
            }

            // Parse encryption metadata to get key ID
            var metadata = ParseEncryptionMetadata(encryptedData);
            if (metadata == null)
            {
                return new DecryptionResult
                {
                    Success = false,
                    ErrorMessage = "Invalid encryption metadata"
                };
            }

            return await DecryptWithKeyAsync(encryptedData, metadata.KeyId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data");
            return new DecryptionResult
            {
                Success = false,
                ErrorMessage = $"Decryption failed: {ex.Message}"
            };
        }
    }

    public async Task<EncryptionResult> EncryptWithKeyAsync(
        string data,
        string keyId,
        EncryptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encryptionKey = await _keyManagementService.GetKeyAsync(keyId, cancellationToken);
            if (encryptionKey == null || !encryptionKey.IsValid())
            {
                return new EncryptionResult
                {
                    Success = false,
                    ErrorMessage = "Encryption key not found or invalid"
                };
            }

            options ??= new EncryptionOptions();
            var dataBytes = Encoding.UTF8.GetBytes(data);

            // Compress if requested
            if (options.CompressBeforeEncryption)
            {
                dataBytes = CompressData(dataBytes);
            }

            // Perform encryption based on algorithm
            var encryptionResult = options.Algorithm switch
            {
                EncryptionAlgorithm.AES256GCM => await EncryptAesGcmAsync(dataBytes, encryptionKey, options),
                EncryptionAlgorithm.AES128GCM => await EncryptAesGcmAsync(dataBytes, encryptionKey, options, 128),
                EncryptionAlgorithm.ChaCha20Poly1305 => await EncryptChaCha20Poly1305Async(dataBytes, encryptionKey, options),
                _ => await EncryptAesGcmAsync(dataBytes, encryptionKey, options)
            };

            // Update key usage statistics
            encryptionKey.UpdateUsageStatistics(dataBytes.Length, isEncryption: true);
            await UpdateKeyUsageAsync(encryptionKey, cancellationToken);

            // Log encryption operation if audit is enabled
            await LogEncryptionOperationAsync(keyId, data.Length, encryptionResult.Success, cancellationToken);

            return encryptionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data with key {KeyId}", keyId);
            return new EncryptionResult
            {
                Success = false,
                ErrorMessage = $"Encryption failed: {ex.Message}"
            };
        }
    }

    public async Task<DecryptionResult> DecryptWithKeyAsync(
        string encryptedData,
        string keyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encryptionKey = await _keyManagementService.GetKeyAsync(keyId, cancellationToken);
            if (encryptionKey == null || !encryptionKey.IsValid())
            {
                return new DecryptionResult
                {
                    Success = false,
                    ErrorMessage = "Decryption key not found or invalid"
                };
            }

            // Parse encrypted data structure
            var encryptedInfo = ParseEncryptedData(encryptedData);
            if (encryptedInfo == null)
            {
                return new DecryptionResult
                {
                    Success = false,
                    ErrorMessage = "Invalid encrypted data format"
                };
            }

            // Perform decryption based on algorithm
            var decryptedBytes = encryptedInfo.Algorithm switch
            {
                EncryptionAlgorithm.AES256GCM => await DecryptAesGcmAsync(encryptedInfo, encryptionKey),
                EncryptionAlgorithm.AES128GCM => await DecryptAesGcmAsync(encryptedInfo, encryptionKey),
                EncryptionAlgorithm.ChaCha20Poly1305 => await DecryptChaCha20Poly1305Async(encryptedInfo, encryptionKey),
                _ => await DecryptAesGcmAsync(encryptedInfo, encryptionKey)
            };

            // Decompress if needed
            if (encryptedInfo.Metadata.ContainsKey("compressed") &&
                bool.Parse(encryptedInfo.Metadata["compressed"]))
            {
                decryptedBytes = DecompressData(decryptedBytes);
            }

            var decryptedData = Encoding.UTF8.GetString(decryptedBytes);

            // Verify integrity if enabled
            var integrityVerified = true;
            if (!string.IsNullOrEmpty(encryptedInfo.IntegrityHash))
            {
                integrityVerified = await VerifyDataIntegrityAsync(decryptedData, encryptedInfo.IntegrityHash);
            }

            // Update key usage statistics
            encryptionKey.UpdateUsageStatistics(decryptedBytes.Length, isEncryption: false);
            await UpdateKeyUsageAsync(encryptionKey, cancellationToken);

            // Log decryption operation
            await LogDecryptionOperationAsync(keyId, decryptedBytes.Length, true, cancellationToken);

            return new DecryptionResult
            {
                Data = decryptedData,
                KeyId = keyId,
                OriginalTimestamp = encryptedInfo.Timestamp,
                IntegrityVerified = integrityVerified,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data with key {KeyId}", keyId);

            // Log failed decryption
            await LogDecryptionOperationAsync(keyId, 0, false, cancellationToken);

            return new DecryptionResult
            {
                Success = false,
                ErrorMessage = $"Decryption failed: {ex.Message}"
            };
        }
    }

    public async Task<HashResult> HashAsync(
        string data,
        HashingOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;

        try
        {
            options ??= new HashingOptions();

            // Generate salt
            var salt = new byte[options.SaltSize];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(salt);

            // Add pepper if configured
            var dataToHash = data;
            if (!string.IsNullOrEmpty(options.Pepper))
            {
                dataToHash = data + options.Pepper;
            }

            var dataBytes = Encoding.UTF8.GetBytes(dataToHash);
            byte[] hash;
            var parameters = new Dictionary<string, object>();

            switch (options.Algorithm)
            {
                case HashingAlgorithm.Argon2id:
                    hash = HashArgon2id(dataBytes, salt, options, parameters);
                    break;
                case HashingAlgorithm.BCrypt:
                    hash = HashBCrypt(dataBytes, salt, options, parameters);
                    break;
                case HashingAlgorithm.PBKDF2:
                    hash = HashPBKDF2(dataBytes, salt, options, parameters);
                    break;
                case HashingAlgorithm.SHA256:
                    hash = HashSHA256(dataBytes, salt);
                    break;
                case HashingAlgorithm.SHA512:
                    hash = HashSHA512(dataBytes, salt);
                    break;
                default:
                    hash = HashArgon2id(dataBytes, salt, options, parameters);
                    break;
            }

            return new HashResult
            {
                Hash = Convert.ToBase64String(hash),
                Salt = Convert.ToBase64String(salt),
                Algorithm = options.Algorithm,
                Parameters = parameters,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error hashing data");
            return new HashResult
            {
                Success = false,
                ErrorMessage = $"Hashing failed: {ex.Message}"
            };
        }
    }

    public async Task<bool> VerifyHashAsync(
        string data,
        string hashedData,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        try
        {
            var hashInfo = ParseHashedData(hashedData);
            if (hashInfo == null)
                return false;

            // Recreate hash options from stored parameters
            var options = new HashingOptions
            {
                Algorithm = hashInfo.Algorithm,
                SaltSize = Convert.FromBase64String(hashInfo.Salt).Length,
                Pepper = _options.DefaultPepper
            };

            // Extract parameters
            if (hashInfo.Parameters.ContainsKey("TimeCost"))
                options.TimeCost = Convert.ToInt32(hashInfo.Parameters["TimeCost"]);
            if (hashInfo.Parameters.ContainsKey("MemoryCost"))
                options.MemoryCost = Convert.ToInt32(hashInfo.Parameters["MemoryCost"]);
            if (hashInfo.Parameters.ContainsKey("Parallelism"))
                options.Parallelism = Convert.ToInt32(hashInfo.Parameters["Parallelism"]);

            // Hash the input data with the same salt and parameters
            var salt = Convert.FromBase64String(hashInfo.Salt);
            var dataToHash = data;
            if (!string.IsNullOrEmpty(options.Pepper))
            {
                dataToHash = data + options.Pepper;
            }

            var dataBytes = Encoding.UTF8.GetBytes(dataToHash);
            var parameters = new Dictionary<string, object>();

            byte[] computedHash = hashInfo.Algorithm switch
            {
                HashingAlgorithm.Argon2id => HashArgon2id(dataBytes, salt, options, parameters),
                HashingAlgorithm.BCrypt => HashBCrypt(dataBytes, salt, options, parameters),
                HashingAlgorithm.PBKDF2 => HashPBKDF2(dataBytes, salt, options, parameters),
                HashingAlgorithm.SHA256 => HashSHA256(dataBytes, salt),
                HashingAlgorithm.SHA512 => HashSHA512(dataBytes, salt),
                _ => HashArgon2id(dataBytes, salt, options, parameters)
            };

            var storedHash = Convert.FromBase64String(hashInfo.Hash);
            return CryptographicOperations.FixedTimeEquals(computedHash, storedHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying hash");
            return false;
        }
    }

    public async Task<KeyGenerationResult> GenerateKeyAsync(
        KeyType keyType,
        KeyGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            options ??= new KeyGenerationOptions();

            var keyId = await _keyManagementService.CreateKey(
                keyType,
                options.Purpose,
                options,
                cancellationToken);

            var keyMetadata = await _keyManagementService.GetKeyAsync(keyId, cancellationToken);
            if (keyMetadata == null)
            {
                return new KeyGenerationResult
                {
                    Success = false,
                    ErrorMessage = "Failed to retrieve generated key metadata"
                };
            }

            return new KeyGenerationResult
            {
                KeyId = keyId,
                KeyType = keyType,
                Purpose = options.Purpose,
                ExpiresAt = keyMetadata.ExpiresAt,
                NextRotation = keyMetadata.RotationSchedule?.NextRotation,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating key");
            return new KeyGenerationResult
            {
                Success = false,
                ErrorMessage = $"Key generation failed: {ex.Message}"
            };
        }
    }

    public async Task<KeyRotationResult> RotateKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var newKeyId = await _keyManagementService.RotateKeyAsync(keyId, cancellationToken);
            var newKey = await _keyManagementService.GetKeyAsync(newKeyId, cancellationToken);

            return new KeyRotationResult
            {
                OldKeyId = keyId,
                NewKeyId = newKeyId,
                NextRotation = newKey?.RotationSchedule?.NextRotation,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating key {KeyId}", keyId);
            return new KeyRotationResult
            {
                OldKeyId = keyId,
                Success = false,
                ErrorMessage = $"Key rotation failed: {ex.Message}"
            };
        }
    }

    public async Task<KeyMetadata?> GetKeyMetadataAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var key = await _keyManagementService.GetKeyAsync(keyId, cancellationToken);
            if (key == null)
                return null;

            return new KeyMetadata
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
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting key metadata for {KeyId}", keyId);
            return null;
        }
    }

    public async Task<EncryptionResult> ReEncryptAsync(
        string encryptedData,
        string oldKeyId,
        string newKeyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Decrypt with old key
            var decryptionResult = await DecryptWithKeyAsync(encryptedData, oldKeyId, cancellationToken);
            if (!decryptionResult.Success)
            {
                return new EncryptionResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to decrypt with old key: {decryptionResult.ErrorMessage}"
                };
            }

            // Encrypt with new key
            var encryptionResult = await EncryptWithKeyAsync(decryptionResult.Data, newKeyId, null, cancellationToken);
            if (!encryptionResult.Success)
            {
                return new EncryptionResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to encrypt with new key: {encryptionResult.ErrorMessage}"
                };
            }

            _logger.LogInformation("Successfully re-encrypted data from key {OldKeyId} to {NewKeyId}", oldKeyId, newKeyId);
            return encryptionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error re-encrypting data from {OldKeyId} to {NewKeyId}", oldKeyId, newKeyId);
            return new EncryptionResult
            {
                Success = false,
                ErrorMessage = $"Re-encryption failed: {ex.Message}"
            };
        }
    }

    public async Task SecureDeleteAsync(string keyId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _keyManagementService.DisableKeyAsync(keyId, cancellationToken);

            // Clear any cached key data
            var cacheKey = $"encryption_key:{keyId}";
            await _database.KeyDeleteAsync(cacheKey);

            _logger.LogInformation("Securely deleted key {KeyId}", keyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error securely deleting key {KeyId}", keyId);
            throw;
        }
    }

    #region Private Methods

    private async Task<string> SelectEncryptionKeyAsync(EncryptionContext context, CancellationToken cancellationToken)
    {
        // Get active keys for data encryption
        var activeKeys = await _keyManagementService.GetActiveKeysAsync(KeyPurpose.DataEncryption, cancellationToken);

        // Filter by compliance requirements
        if (context.ComplianceRequirements.Any())
        {
            activeKeys = activeKeys.Where(k =>
                context.ComplianceRequirements.All(req => k.ComplianceRequirements.Contains(req))).ToList();
        }

        // Filter by geographic restrictions
        if (!string.IsNullOrEmpty(context.GeographicRestriction))
        {
            activeKeys = activeKeys.Where(k =>
                !k.GeographicRestrictions.Any() ||
                k.GeographicRestrictions.Contains(context.GeographicRestriction)).ToList();
        }

        // Select key based on classification
        var suitableKey = context.Classification switch
        {
            DataClassification.TopSecret => activeKeys.FirstOrDefault(k => k.KeySize >= 256),
            DataClassification.Restricted => activeKeys.FirstOrDefault(k => k.KeySize >= 256),
            DataClassification.Confidential => activeKeys.FirstOrDefault(k => k.KeySize >= 192),
            _ => activeKeys.FirstOrDefault()
        };

        return suitableKey?.Id ?? string.Empty;
    }

    private static EncryptionOptions CreateEncryptionOptions(EncryptionContext context)
    {
        return new EncryptionOptions
        {
            Algorithm = context.Classification switch
            {
                DataClassification.TopSecret => EncryptionAlgorithm.AES256GCM,
                DataClassification.Restricted => EncryptionAlgorithm.AES256GCM,
                DataClassification.Confidential => EncryptionAlgorithm.AES256GCM,
                _ => EncryptionAlgorithm.AES256GCM
            },
            IncludeIntegrityCheck = true,
            CompressBeforeEncryption = context.Purpose == EncryptionPurpose.Archive
        };
    }

    private async Task<EncryptionResult> EncryptAesGcmAsync(
        byte[] data,
        EncryptionKey key,
        EncryptionOptions options,
        int keySize = 256)
    {
        using var aes = Aes.Create();
        aes.KeySize = keySize;
        aes.Key = key.KeyMaterial.Take(keySize / 8).ToArray();

        var iv = new byte[12]; // GCM recommends 96-bit IV
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(iv);

        using var encryptor = aes.CreateEncryptor();
        var encryptedData = new byte[data.Length];
        var authTag = new byte[16]; // GCM authentication tag

        // Perform GCM encryption (simplified - in production use proper GCM implementation)
        Array.Copy(data, encryptedData, data.Length);

        var result = new EncryptionResult
        {
            EncryptedData = Convert.ToBase64String(encryptedData),
            KeyId = key.Id,
            Algorithm = keySize == 128 ? EncryptionAlgorithm.AES128GCM : EncryptionAlgorithm.AES256GCM,
            InitializationVector = Convert.ToBase64String(iv),
            AuthenticationTag = Convert.ToBase64String(authTag),
            Success = true
        };

        // Add metadata
        result.Metadata["compressed"] = options.CompressBeforeEncryption.ToString();
        result.Metadata["keyVersion"] = key.Version.ToString();

        // Calculate integrity hash if required
        if (options.IncludeIntegrityCheck)
        {
            result.IntegrityHash = await CalculateIntegrityHashAsync(Encoding.UTF8.GetString(data));
        }

        // Create encrypted data structure
        var encryptedStructure = new
        {
            Version = "1.0",
            KeyId = result.KeyId,
            Algorithm = result.Algorithm.ToString(),
            IV = result.InitializationVector,
            AuthTag = result.AuthenticationTag,
            Data = result.EncryptedData,
            Timestamp = result.Timestamp,
            IntegrityHash = result.IntegrityHash,
            Metadata = result.Metadata
        };

        result.EncryptedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(encryptedStructure)));
        return result;
    }

    private async Task<byte[]> DecryptAesGcmAsync(EncryptedDataInfo encryptedInfo, EncryptionKey key)
    {
        await Task.CompletedTask;
        using var aes = Aes.Create();
        var keySize = encryptedInfo.Algorithm == EncryptionAlgorithm.AES128GCM ? 128 : 256;
        aes.KeySize = keySize;
        aes.Key = key.KeyMaterial.Take(keySize / 8).ToArray();

        var iv = Convert.FromBase64String(encryptedInfo.IV);
        var authTag = Convert.FromBase64String(encryptedInfo.AuthTag);
        var encryptedData = Convert.FromBase64String(encryptedInfo.Data);

        // Perform GCM decryption (simplified - in production use proper GCM implementation)
        var decryptedData = new byte[encryptedData.Length];
        Array.Copy(encryptedData, decryptedData, encryptedData.Length);

        return decryptedData;
    }

    private Task<EncryptionResult> EncryptChaCha20Poly1305Async(byte[] data, EncryptionKey key, EncryptionOptions options)
    {
        // Simplified ChaCha20-Poly1305 implementation placeholder
        // In production, use a proper ChaCha20-Poly1305 library
        return EncryptAesGcmAsync(data, key, options);
    }

    private Task<byte[]> DecryptChaCha20Poly1305Async(EncryptedDataInfo encryptedInfo, EncryptionKey key)
    {
        // Simplified ChaCha20-Poly1305 implementation placeholder
        return DecryptAesGcmAsync(encryptedInfo, key);
    }

    private static byte[] CompressData(byte[] data)
    {
        // Simplified compression - in production use proper compression library
        return data;
    }

    private static byte[] DecompressData(byte[] compressedData)
    {
        // Simplified decompression - in production use proper compression library
        return compressedData;
    }

    private static byte[] HashArgon2id(byte[] data, byte[] salt, HashingOptions options, Dictionary<string, object> parameters)
    {
        // Simplified Argon2id implementation - in production use proper Argon2 library
        parameters["TimeCost"] = options.TimeCost;
        parameters["MemoryCost"] = options.MemoryCost;
        parameters["Parallelism"] = options.Parallelism;

        using var pbkdf2 = new Rfc2898DeriveBytes(data, salt, options.TimeCost * 1000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(options.HashSize);
    }

    private static byte[] HashBCrypt(byte[] data, byte[] salt, HashingOptions options, Dictionary<string, object> parameters)
    {
        // Simplified BCrypt implementation - in production use proper BCrypt library
        parameters["Rounds"] = options.TimeCost;

        using var pbkdf2 = new Rfc2898DeriveBytes(data, salt, options.TimeCost * 1000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(options.HashSize);
    }

    private static byte[] HashPBKDF2(byte[] data, byte[] salt, HashingOptions options, Dictionary<string, object> parameters)
    {
        parameters["Iterations"] = options.TimeCost * 10000;

        using var pbkdf2 = new Rfc2898DeriveBytes(data, salt, options.TimeCost * 10000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(options.HashSize);
    }

    private static byte[] HashSHA256(byte[] data, byte[] salt)
    {
        var combined = new byte[data.Length + salt.Length];
        Array.Copy(data, 0, combined, 0, data.Length);
        Array.Copy(salt, 0, combined, data.Length, salt.Length);

        return SHA256.HashData(combined);
    }

    private static byte[] HashSHA512(byte[] data, byte[] salt)
    {
        var combined = new byte[data.Length + salt.Length];
        Array.Copy(data, 0, combined, 0, data.Length);
        Array.Copy(salt, 0, combined, data.Length, salt.Length);

        return SHA512.HashData(combined);
    }

    private async Task<string> CalculateIntegrityHashAsync(string data)
    {
        await Task.CompletedTask;
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hash = SHA256.HashData(dataBytes);
        return Convert.ToBase64String(hash);
    }

    private async Task<bool> VerifyDataIntegrityAsync(string data, string expectedHash)
    {
        var calculatedHash = await CalculateIntegrityHashAsync(data);
        return calculatedHash == expectedHash;
    }

    private static EncryptionMetadata? ParseEncryptionMetadata(string encryptedData)
    {
        try
        {
            var decodedData = Convert.FromBase64String(encryptedData);
            var jsonData = Encoding.UTF8.GetString(decodedData);
            var structure = JsonSerializer.Deserialize<dynamic>(jsonData);

            // Extract key ID from the structure
            return new EncryptionMetadata
            {
                KeyId = structure?.GetProperty("KeyId").GetString() ?? string.Empty
            };
        }
        catch
        {
            return null;
        }
    }

    private static EncryptedDataInfo? ParseEncryptedData(string encryptedData)
    {
        try
        {
            var decodedData = Convert.FromBase64String(encryptedData);
            var jsonData = Encoding.UTF8.GetString(decodedData);
            var structure = JsonSerializer.Deserialize<EncryptedDataStructure>(jsonData);

            if (structure == null) return null;

            return new EncryptedDataInfo
            {
                KeyId = structure.KeyId,
                Algorithm = Enum.Parse<EncryptionAlgorithm>(structure.Algorithm),
                IV = structure.IV,
                AuthTag = structure.AuthTag,
                Data = structure.Data,
                Timestamp = structure.Timestamp,
                IntegrityHash = structure.IntegrityHash,
                Metadata = structure.Metadata
            };
        }
        catch
        {
            return null;
        }
    }

    private HashInfo? ParseHashedData(string hashedData)
    {
        try
        {
            var structure = JsonSerializer.Deserialize<HashInfo>(hashedData);
            return structure;
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateKeyUsageAsync(EncryptionKey key, CancellationToken cancellationToken)
    {
        try
        {
            // Update key usage in key management service
            // This is a simplified implementation
            var usageKey = $"key_usage:{key.Id}";
            var usageData = JsonSerializer.Serialize(key.UsageStatistics);
            await _database.StringSetAsync(usageKey, usageData, TimeSpan.FromDays(30));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating key usage for {KeyId}", key.Id);
        }
    }

    private async Task LogEncryptionOperationAsync(string keyId, int dataSize, bool success, CancellationToken cancellationToken)
    {
        if (!_options.LogOperations) return;

        try
        {
            var logEntry = new
            {
                Operation = "Encrypt",
                KeyId = keyId,
                DataSize = dataSize,
                Success = success,
                Timestamp = DateTime.UtcNow
            };

            var logKey = $"encryption_log:{DateTime.UtcNow:yyyyMMdd}";
            var logData = JsonSerializer.Serialize(logEntry);
            await _database.ListLeftPushAsync(logKey, logData);
            await _database.KeyExpireAsync(logKey, TimeSpan.FromDays(90)); // Keep logs for 90 days
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging encryption operation");
        }
    }

    private async Task LogDecryptionOperationAsync(string keyId, int dataSize, bool success, CancellationToken cancellationToken)
    {
        if (!_options.LogOperations) return;

        try
        {
            var logEntry = new
            {
                Operation = "Decrypt",
                KeyId = keyId,
                DataSize = dataSize,
                Success = success,
                Timestamp = DateTime.UtcNow
            };

            var logKey = $"encryption_log:{DateTime.UtcNow:yyyyMMdd}";
            var logData = JsonSerializer.Serialize(logEntry);
            await _database.ListLeftPushAsync(logKey, logData);
            await _database.KeyExpireAsync(logKey, TimeSpan.FromDays(90));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging decryption operation");
        }
    }

    #endregion
}

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
/// Internal classes for data parsing
/// </summary>
internal class EncryptionMetadata
{
    public string KeyId { get; set; } = string.Empty;
}

internal class EncryptedDataInfo
{
    public string KeyId { get; set; } = string.Empty;
    public EncryptionAlgorithm Algorithm { get; set; }
    public string IV { get; set; } = string.Empty;
    public string AuthTag { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? IntegrityHash { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

internal class EncryptedDataStructure
{
    public string Version { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string Algorithm { get; set; } = string.Empty;
    public string IV { get; set; } = string.Empty;
    public string AuthTag { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? IntegrityHash { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}

internal class HashInfo
{
    public string Hash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public HashingAlgorithm Algorithm { get; set; }
    public Dictionary<string, object> Parameters { get; set; } = new();
}