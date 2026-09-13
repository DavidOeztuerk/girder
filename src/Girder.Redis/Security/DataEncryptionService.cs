using Girder.Redis.Security.Encryption;
using Girder.Abstractions.Security.Secrets;
using Girder.Abstractions.Security.Encryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Girder.Redis.Security.Encryption;

/// <summary>
/// Advanced data encryption service with key management
/// </summary>
public class DataEncryptionService : IDataEncryptionService
{
    private readonly IKeyManagementService _keyManagementService;
    private readonly ILogger<DataEncryptionService> _logger;
    private readonly DataEncryptionOptions _options;
    private readonly IDatabase _database;

    /// <summary>96 bits — the nonce size GCM is specified for.</summary>
    private const int NonceBytes = 12;

    /// <summary>128 bits — the full GCM tag, never a truncated one.</summary>
    private const int TagBytes = 16;

    /// <summary>
    /// The envelope written since 4.4.2. Its GCM tag authenticates the
    /// ciphertext and every semantic field in the surrounding envelope.
    /// </summary>
    private const string EnvelopeVersion = "2.1";

    /// <summary>
    /// Girder 4.4.1 encrypted the payload, but authenticated only caller AAD;
    /// envelope control metadata could still be altered independently.
    /// </summary>
    private const string UnboundMetadataEnvelopeVersion = "2.0";

    /// <summary>The envelope written up to and including 4.4.0.</summary>
    private const string PreFixEnvelopeVersion = "1.0";

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
                    IntegrityVerified = false,
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
                    IntegrityVerified = false,
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
                IntegrityVerified = false,
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

            // Only algorithms that are actually implemented are accepted.
            // Until 4.4.1 the default arm fell through to the AES branch, so
            // asking for ChaCha20-Poly1305 or CBC returned an envelope stamped
            // "AES256GCM" — a construction the caller had chosen against,
            // substituted silently. That is the same class of defect as not
            // encrypting at all, and it is refused the same way HashAsync
            // refuses the memory-hard algorithms it does not ship.
            var encryptionResult = options.Algorithm switch
            {
                EncryptionAlgorithm.AES256GCM => await EncryptAesGcmAsync(dataBytes, encryptionKey, options),
                EncryptionAlgorithm.AES128GCM => await EncryptAesGcmAsync(dataBytes, encryptionKey, options, 128),
                _ => throw new NotSupportedException(
                    $"{options.Algorithm} is not implemented. Girder ships AES256GCM and "
                    + "AES128GCM; the remaining members of EncryptionAlgorithm are declared "
                    + "but not provided, and one of them is refused rather than substituted.")
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
                    IntegrityVerified = false,
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
                    IntegrityVerified = false,
                    ErrorMessage = "Invalid encrypted data format"
                };
            }

            // Mirrors EncryptWithKeyAsync exactly. Decryption must not accept
            // an algorithm that encryption refuses, or an envelope could be
            // read back through a different construction than the one that
            // supposedly produced it.
            var decryptedBytes = encryptedInfo.Algorithm switch
            {
                EncryptionAlgorithm.AES256GCM => await DecryptAesGcmAsync(encryptedInfo, encryptionKey),
                EncryptionAlgorithm.AES128GCM => await DecryptAesGcmAsync(encryptedInfo, encryptionKey),
                _ => throw new NotSupportedException(
                    $"{encryptedInfo.Algorithm} is not implemented and cannot be decrypted.")
            };

            // Decompress if needed
            if (encryptedInfo.Metadata.TryGetValue("compressed", out var compressedFlag) &&
                bool.TryParse(compressedFlag, out var wasCompressed) && wasCompressed)
            {
                decryptedBytes = DecompressData(decryptedBytes);
            }

            var decryptedData = Encoding.UTF8.GetString(decryptedBytes);

            // Integrity is neither a separate step nor optional any more:
            // AES-GCM authenticates while it decrypts, so a wrong key, a
            // flipped ciphertext bit, an altered IV or a touched AAD have all
            // thrown above and never reach this line.
            //
            // Up to 4.4.0 this block compared a SHA-256 of the PLAINTEXT that
            // was stored in the envelope next to the ciphertext. That is a
            // second, separate defect from the missing encryption: such a hash
            // is an oracle — whoever can read the store can try candidates
            // against it without ever touching a key — and it was useless as a
            // check besides, because a foreign key reproduced it exactly and
            // reported IntegrityVerified = true.

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
                IntegrityVerified = true,
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
                IntegrityVerified = false,
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

            // Only algorithms that are actually implemented are accepted. The
            // memory-hard ones are not, and returning PBKDF2 under their name
            // would hand the caller the very construction they chose against.
            hash = options.Algorithm switch
            {
                HashingAlgorithm.PBKDF2 => HashPBKDF2(dataBytes, salt, options, parameters),
                HashingAlgorithm.SHA256 => HashSHA256(dataBytes, salt),
                HashingAlgorithm.SHA512 => HashSHA512(dataBytes, salt),

                HashingAlgorithm.Argon2id or HashingAlgorithm.Argon2i or HashingAlgorithm.Argon2d
                    or HashingAlgorithm.BCrypt or HashingAlgorithm.SCrypt =>
                    throw new NotSupportedException(
                        $"{options.Algorithm} is not implemented. Girder ships no memory-hard "
                        + "hashing; use PBKDF2, or supply your own IDataEncryptionService."),

                _ => throw new NotSupportedException($"Unknown hashing algorithm: {options.Algorithm}.")
            };

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

            // Mirrors HashAsync exactly. Verification must not accept an
            // algorithm that hashing refuses, or a stored hash could be checked
            // against a different construction than the one that produced it.
            byte[] computedHash = hashInfo.Algorithm switch
            {
                HashingAlgorithm.PBKDF2 => HashPBKDF2(dataBytes, salt, options, parameters),
                HashingAlgorithm.SHA256 => HashSHA256(dataBytes, salt),
                HashingAlgorithm.SHA512 => HashSHA512(dataBytes, salt),
                _ => throw new NotSupportedException(
                    $"{hashInfo.Algorithm} is not implemented and cannot be verified.")
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

    /// <summary>
    /// AES-GCM, by way of <see cref="AesGcm"/>.
    ///
    /// WHY THIS IS IMPLEMENTED RATHER THAN REFUSED. Two ways out of the 4.4.0
    /// defect were open: implement the algorithm the envelope has always
    /// claimed, or throw <see cref="NotSupportedException"/> and say plainly
    /// that Girder ships no encryption. The first was chosen because the
    /// counter-checks that prove it are cheap and exact — a foreign key, one
    /// flipped bit, two ciphertexts from one plaintext, and a byte-subsequence
    /// search for the plaintext in the raw result — and because
    /// <see cref="AesGcm"/> is in the base class library, so implementing it
    /// adds no dependency and nothing to trust beyond .NET itself. Refusal
    /// would have been the honest answer only if the guarantee could not be
    /// checked. Here it can be, and it is, in
    /// <c>DataEncryptionServiceCipherTests</c>.
    ///
    /// What 4.4.0 did instead: <c>Array.Copy(data, encryptedData, data.Length)</c>
    /// under a comment reading "simplified - in production use proper GCM
    /// implementation", an all-zero authentication tag, an IV drawn and never
    /// used, and a SHA-256 of the plaintext stored beside it.
    /// </summary>
    private async Task<EncryptionResult> EncryptAesGcmAsync(
        byte[] data,
        EncryptionKey key,
        EncryptionOptions options,
        int keySize = 256)
    {
        await Task.CompletedTask;

        // A fresh nonce per operation, from the OS CSPRNG. A repeated nonce
        // under one GCM key does not merely weaken that ciphertext: it
        // discloses the XOR of the two plaintexts and forfeits authentication
        // for the key. That is why one counter-check encrypts the same
        // plaintext twice and insists the results differ.
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var algorithm = keySize == 128 ? EncryptionAlgorithm.AES128GCM : EncryptionAlgorithm.AES256GCM;
        var initializationVector = Convert.ToBase64String(nonce);
        var callerAad = options.AdditionalData is { Length: > 0 }
            ? Convert.ToBase64String(options.AdditionalData)
            : null;
        var timestamp = DateTime.UtcNow;
        var metadata = new Dictionary<string, string>
        {
            ["compressed"] = options.CompressBeforeEncryption.ToString(),
            ["keyVersion"] = key.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        // GCM authenticates the ciphertext itself. Its associated data binds
        // every surrounding field that changes how the plaintext is selected,
        // interpreted or reported. The binary representation is length-prefixed
        // and metadata keys are sorted, so it has exactly one canonical form.
        var associatedData = BuildEnvelopeAssociatedData(
            EnvelopeVersion,
            key.Id,
            algorithm.ToString(),
            initializationVector,
            callerAad,
            timestamp,
            integrityHash: null,
            metadata);

        var keyBytes = DeriveAesKey(key, keySize);
        var ciphertext = new byte[data.Length];
        var authTag = new byte[TagBytes];

        try
        {
            using var aesGcm = new AesGcm(keyBytes, TagBytes);
            aesGcm.Encrypt(nonce, data, ciphertext, authTag, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }

        var result = new EncryptionResult
        {
            EncryptedData = Convert.ToBase64String(ciphertext),
            KeyId = key.Id,
            Algorithm = algorithm,
            InitializationVector = initializationVector,
            AuthenticationTag = Convert.ToBase64String(authTag),
            Timestamp = timestamp,

            // Deliberately null. The GCM tag above IS the integrity check.
            // Anything derived from the plaintext and stored next to the
            // ciphertext is an oracle, not a checksum; see DecryptWithKeyAsync.
            IntegrityHash = null,
            Metadata = metadata,
            Success = true
        };

        var encryptedStructure = new
        {
            Version = EnvelopeVersion,
            KeyId = result.KeyId,
            Algorithm = result.Algorithm.ToString(),
            IV = result.InitializationVector,
            AuthTag = result.AuthenticationTag,
            Data = result.EncryptedData,

            // Associated data is authenticated, not encrypted — storing it in
            // the clear is what it is for. It has to be stored, because
            // DecryptWithKeyAsync takes no options and could not otherwise
            // supply it. Until 4.4.1 EncryptionOptions.AdditionalData was read
            // by nothing at all.
            Aad = callerAad,
            Timestamp = result.Timestamp,
            IntegrityHash = result.IntegrityHash,
            Metadata = result.Metadata
        };

        result.EncryptedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(encryptedStructure)));
        return result;
    }

    /// <summary>
    /// The inverse. Throws rather than return anything it cannot
    /// authenticate — a wrong key, a changed byte, a changed IV, a changed
    /// AAD and a truncated tag all end here, and the caller sees
    /// <c>Success = false</c>.
    /// </summary>
    /// <remarks>
    /// Note what is deliberately NOT done: the key id in the envelope is not
    /// compared with the key id that was asked for. Comparing them would let
    /// the wrong-key counter-check pass for the wrong reason — on a string
    /// comparison instead of on the cryptography. The tag is the check.
    /// </remarks>
    private async Task<byte[]> DecryptAesGcmAsync(EncryptedDataInfo encryptedInfo, EncryptionKey key)
    {
        await Task.CompletedTask;

        if (encryptedInfo.Version == PreFixEnvelopeVersion)
        {
            throw new CryptographicException(
                "This envelope was written by Girder 4.4.0 or earlier, whose Data field holds "
                + "the PLAINTEXT Base64-encoded and whose AuthTag is all zeroes — it was never "
                + "encrypted (advisory: Girder.Redis, fixed in 4.4.1). It is refused rather "
                + "than read back as though decryption had succeeded. Read such values with "
                + "the version that wrote them, then store them again with 4.4.2 or later.");
        }

        if (encryptedInfo.Version == UnboundMetadataEnvelopeVersion)
        {
            throw new CryptographicException(
                "This envelope was written by Girder 4.4.1. Its payload is encrypted, but "
                + "its control metadata is not authenticated, so IntegrityVerified cannot "
                + "honestly be reported. Read it with 4.4.1 and store it again with 4.4.2 "
                + "or later.");
        }

        if (encryptedInfo.Version != EnvelopeVersion)
        {
            throw new CryptographicException(
                $"Unknown encryption envelope version '{encryptedInfo.Version}'.");
        }

        var keySize = encryptedInfo.Algorithm == EncryptionAlgorithm.AES128GCM ? 128 : 256;
        var keyBytes = DeriveAesKey(key, keySize);

        var nonce = Convert.FromBase64String(encryptedInfo.IV);
        var authTag = Convert.FromBase64String(encryptedInfo.AuthTag);
        var ciphertext = Convert.FromBase64String(encryptedInfo.Data);
        var associatedData = BuildEnvelopeAssociatedData(
            encryptedInfo.Version,
            encryptedInfo.KeyId,
            encryptedInfo.AlgorithmName,
            encryptedInfo.IV,
            encryptedInfo.Aad,
            encryptedInfo.Timestamp,
            encryptedInfo.IntegrityHash,
            encryptedInfo.Metadata);

        // Both sizes come out of the stored envelope, so both are attacker
        // input wherever the store is. AesGcm accepts a tag of 12 to 16 bytes,
        // and every byte dropped is eight bits of forgery resistance given
        // away, so only the full size is taken.
        if (authTag.Length != TagBytes)
        {
            throw new CryptographicException(
                $"Authentication tag is {authTag.Length} bytes; {TagBytes} are required.");
        }

        if (nonce.Length != NonceBytes)
        {
            throw new CryptographicException(
                $"Initialization vector is {nonce.Length} bytes; {NonceBytes} are required.");
        }

        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(keyBytes, TagBytes);

            // Throws AuthenticationTagMismatchException — a CryptographicException —
            // if anything at all was altered, and clears the destination buffer
            // before it does.
            aesGcm.Decrypt(nonce, ciphertext, authTag, plaintext, associatedData);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }

        return plaintext;
    }

    /// <summary>
    /// Produces the one binary representation authenticated for an envelope.
    /// Ciphertext and tag are excluded because GCM covers the former directly
    /// and the latter cannot contain itself.
    /// </summary>
    private static byte[] BuildEnvelopeAssociatedData(
        string version,
        string keyId,
        string algorithm,
        string initializationVector,
        string? callerAad,
        DateTime timestamp,
        string? integrityHash,
        IReadOnlyDictionary<string, string> metadata)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, new UTF8Encoding(false, true), leaveOpen: true);

        WriteRequired(writer, "Girder.Redis.EncryptionEnvelope");
        WriteRequired(writer, version);
        WriteRequired(writer, keyId);
        WriteRequired(writer, algorithm);
        WriteRequired(writer, initializationVector);
        WriteOptional(writer, callerAad);
        writer.Write(timestamp.ToUniversalTime().Ticks);
        WriteOptional(writer, integrityHash);

        writer.Write(metadata.Count);
        foreach (var entry in metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            WriteRequired(writer, entry.Key);
            WriteRequired(writer, entry.Value);
        }

        writer.Flush();
        return buffer.ToArray();
    }

    private static void WriteRequired(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static void WriteOptional(BinaryWriter writer, string? value)
    {
        writer.Write(value is not null);
        if (value is not null)
        {
            WriteRequired(writer, value);
        }
    }

    /// <summary>
    /// The key material for one operation, checked for length.
    /// </summary>
    /// <remarks>
    /// 4.4.0 wrote <c>key.KeyMaterial.Take(keySize / 8).ToArray()</c>, which
    /// yields whatever is there when the key is short — so a 128-bit key used
    /// where AES-256 was asked for would have gone through without a word.
    /// </remarks>
    private static byte[] DeriveAesKey(EncryptionKey key, int keySizeBits)
    {
        var required = keySizeBits / 8;

        if (key.KeyMaterial.Length < required)
        {
            throw new CryptographicException(
                $"Key '{key.Id}' carries {key.KeyMaterial.Length} bytes of material; "
                + $"AES-{keySizeBits}-GCM requires {required}.");
        }

        return key.KeyMaterial.AsSpan(0, required).ToArray();
    }

    /// <summary>
    /// Gzip, because <c>CompressBeforeEncryption</c> said so and until 4.4.1
    /// this method returned its input unchanged while the envelope recorded
    /// <c>compressed=true</c>.
    /// </summary>
    /// <remarks>
    /// Compressing before encrypting leaks something about the plaintext
    /// through the ciphertext length, and where an attacker can both influence
    /// part of a value and observe its stored size, that leak is exploitable
    /// (the CRIME/BREACH family). The option is off by default and stays a
    /// deliberate choice.
    /// </remarks>
    private static byte[] CompressData(byte[] data)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    private static byte[] DecompressData(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] HashPBKDF2(byte[] data, byte[] salt, HashingOptions options, Dictionary<string, object> parameters)
    {
        var iterations = options.TimeCost * 10000;
        parameters["Iterations"] = iterations;

        return Rfc2898DeriveBytes.Pbkdf2(data, salt, iterations, HashAlgorithmName.SHA256, options.HashSize);
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
                Version = structure.Version,
                KeyId = structure.KeyId,
                Algorithm = Enum.Parse<EncryptionAlgorithm>(structure.Algorithm),
                AlgorithmName = structure.Algorithm,
                IV = structure.IV,
                AuthTag = structure.AuthTag,
                Data = structure.Data,
                Aad = structure.Aad,
                Timestamp = structure.Timestamp,
                IntegrityHash = structure.IntegrityHash,
                Metadata = structure.Metadata ?? new Dictionary<string, string>()
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
/// Internal classes for data parsing
/// </summary>
internal class EncryptionMetadata
{
    public string KeyId { get; set; } = string.Empty;
}

internal class EncryptedDataInfo
{
    public string Version { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public EncryptionAlgorithm Algorithm { get; set; }
    public string AlgorithmName { get; set; } = string.Empty;
    public string IV { get; set; } = string.Empty;
    public string AuthTag { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string? Aad { get; set; }
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
    public string? Aad { get; set; }
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
