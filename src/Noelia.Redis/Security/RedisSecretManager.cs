using Noelia.Abstractions.Security.Encryption;
using Noelia.Abstractions.Security.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Noelia.Redis.Security;

/// <summary>
/// Redis-based secure secret manager with encryption
/// </summary>
public class SecretManager : IVersionedSecretProvider
{
    private const int CurrentFormatVersion = 2;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IDatabase _database;
    private readonly ILogger<SecretManager> _logger;
    private readonly string _keyPrefix;
    private readonly byte[] _encryptionKey;

    public SecretManager(
        IConnectionMultiplexer connectionMultiplexer,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<SecretManager> logger)
    {
        _database = connectionMultiplexer.GetDatabase();
        _logger = logger;
        _keyPrefix = "secrets:";

        // Get encryption key from environment variable first, then config
        var encryptionKeyBase64 = Environment.GetEnvironmentVariable("SECRET_MANAGER_ENCRYPTION_KEY_BASE64")
            ?? configuration["SecretManager:EncryptionKeyBase64"]
            ?? configuration["SecretManager:EncryptionKey"]; // Legacy support

        if (string.IsNullOrEmpty(encryptionKeyBase64))
        {
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Persistent encryption key not configured. Set SECRET_MANAGER_ENCRYPTION_KEY_BASE64 " +
                    "environment variable or SecretManager:EncryptionKeyBase64 in configuration. " +
                    "All services MUST use the SAME key for proper authentication.");
            }

            // Development: generate a process-local key, but never put key
            // material into a log, structured property, exception or hint.
            _encryptionKey = GenerateEncryptionKey();
            _logger.LogWarning(
                "No encryption key found in configuration. Generated a transient process-local key " +
                "for development only. Configure SECRET_MANAGER_ENCRYPTION_KEY_BASE64 before " +
                "storing persistent or shared secrets.");
        }
        else
        {
            _encryptionKey = Convert.FromBase64String(encryptionKeyBase64);

            if (_encryptionKey.Length != 32)
            {
                throw new InvalidOperationException(
                    $"The SecretManager encryption key is {_encryptionKey.Length} bytes; 32 are required.");
            }

            _logger.LogInformation("Encryption key loaded successfully from configuration");
        }
    }

    public async Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetSecretKey(name);
            var encryptedData = await _database.StringGetAsync(key);
            
            if (!encryptedData.HasValue)
            {
                return null;
            }

            var secretData = JsonSerializer.Deserialize<EncryptedSecretData>((string)encryptedData!);
            if (secretData == null)
            {
                return null;
            }

            // Authenticate every field that controls how this record is
            // interpreted before trusting even its expiry metadata.
            var decryptedValue = DecryptSecret(name, secretData);

            if (secretData.ExpiresAt.HasValue && secretData.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Attempted to access expired secret: {SecretName}", name);
                return null;
            }
            
            _logger.LogDebug("Secret retrieved: {SecretName}", name);
            return decryptedValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret: {SecretName}", name);
            return null;
        }
    }

    public async Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await GetNextVersionAsync(name);
            var secretData = new EncryptedSecretData
            {
                Name = name,
                FormatVersion = CurrentFormatVersion,
                CreatedAt = DateTime.UtcNow,
                Version = version,
                IsActive = true,
                CreatedBy = "System"
            };
            var (encryptedValue, nonce, authenticationTag) = EncryptSecret(value, secretData);
            secretData.EncryptedValue = encryptedValue;
            secretData.IV = nonce;
            secretData.AuthenticationTag = authenticationTag;

            var key = GetSecretKey(name);
            var serializedData = JsonSerializer.Serialize(secretData);
            
            await _database.StringSetAsync(key, serializedData);
            
            // Store in history
            await StoreSecretHistoryAsync(secretData);
            
            _logger.LogInformation("Secret set: {SecretName}, Version: {Version}", name, secretData.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set secret: {SecretName}", name);
            throw;
        }
    }

    public async Task<string> RotateSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate new secret value
            var newValue = GenerateSecretValue();
            
            // Deactivate current version
            await DeactivateCurrentVersionAsync(name);
            
            // Set new version
            await SetSecretAsync(name, newValue, cancellationToken);
            
            _logger.LogInformation("Secret rotated: {SecretName}", name);
            return newValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate secret: {SecretName}", name);
            throw;
        }
    }

    public async Task<string?> GetSecretVersionAsync(
        string key,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(version, out var versionNumber))
        {
            return null;
        }

        try
        {
            var historyData = await _database.ListRangeAsync(GetSecretHistoryKey(key));
            foreach (var item in historyData)
            {
                if (!item.HasValue)
                {
                    continue;
                }

                var secretData = JsonSerializer.Deserialize<EncryptedSecretData>((string)item!);
                if (secretData?.Version == versionNumber)
                {
                    return DecryptSecret(key, secretData);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get a secret version: {SecretName}", key);
            return null;
        }
    }

    public async Task<IEnumerable<SecretVersion>> ListSecretVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var historyKey = GetSecretHistoryKey(name);
            var historyData = await _database.ListRangeAsync(historyKey);
            
            var versions = new List<SecretVersion>();
            
            foreach (var item in historyData)
            {
                if (item.HasValue)
                {
                    var secretData = JsonSerializer.Deserialize<EncryptedSecretData>((string)item!);
                    if (secretData != null)
                    {
                        versions.Add(new SecretVersion
                        {
                            Name = secretData.Name,
                            Version = secretData.Version,
                            CreatedAt = secretData.CreatedAt,
                            ExpiresAt = secretData.ExpiresAt,
                            IsActive = secretData.IsActive,
                            CreatedBy = secretData.CreatedBy
                        });
                    }
                }
            }
            
            return versions.OrderByDescending(v => v.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret history: {SecretName}", name);
            return Enumerable.Empty<SecretVersion>();
        }
    }

    public async Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetSecretKey(name);
            var historyKey = GetSecretHistoryKey(name);
            
            await _database.KeyDeleteAsync(new RedisKey[] { key, historyKey });
            
            _logger.LogInformation("Secret deleted: {SecretName}", name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete secret: {SecretName}", name);
            throw;
        }
    }

    public async Task<bool> SecretExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = GetSecretKey(name);
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check secret existence: {SecretName}", name);
            return false;
        }
    }

    public async Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        try
        {
            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
            var pattern = $"{_keyPrefix}*";
            var keys = server.Keys(pattern: pattern);

            var secretNames = new List<string>();

            foreach (var key in keys)
            {
                var keyStr = key.ToString();
                if (keyStr.StartsWith(_keyPrefix) && !keyStr.Contains(":history:"))
                {
                    var secretName = keyStr.Substring(_keyPrefix.Length);
                    secretNames.Add(secretName);
                }
            }

            return secretNames;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get secret names");
            return Enumerable.Empty<string>();
        }
    }

    private async Task<int> GetNextVersionAsync(string name)
    {
        var historyKey = GetSecretHistoryKey(name);
        var count = await _database.ListLengthAsync(historyKey);
        return (int)count + 1;
    }

    private async Task StoreSecretHistoryAsync(EncryptedSecretData secretData)
    {
        var historyKey = GetSecretHistoryKey(secretData.Name);
        var serializedData = JsonSerializer.Serialize(secretData);
        
        await _database.ListLeftPushAsync(historyKey, serializedData);
        
        // Keep only the last 10 versions
        await _database.ListTrimAsync(historyKey, 0, 9);
    }

    private async Task DeactivateCurrentVersionAsync(string name)
    {
        var key = GetSecretKey(name);
        var encryptedData = await _database.StringGetAsync(key);
        
        if (encryptedData.HasValue)
        {
            var secretData = JsonSerializer.Deserialize<EncryptedSecretData>((string)encryptedData!);
            if (secretData != null)
            {
                secretData.IsActive = false;
                await StoreSecretHistoryAsync(secretData);
            }
        }
    }

    private (string encryptedValue, string nonce, string authenticationTag) EncryptSecret(
        string value,
        EncryptedSecretData secretData)
    {
        var plaintext = Encoding.UTF8.GetBytes(value);
        var encrypted = new byte[plaintext.Length];
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var authenticationTag = new byte[TagSize];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Encrypt(
            nonce,
            plaintext,
            encrypted,
            authenticationTag,
            AssociatedData(secretData.Name, secretData));

        return (
            Convert.ToBase64String(encrypted),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(authenticationTag));
    }

    private string DecryptSecret(string requestedName, EncryptedSecretData secretData)
    {
        if (secretData.FormatVersion != CurrentFormatVersion
            || string.IsNullOrWhiteSpace(secretData.AuthenticationTag))
        {
            throw new CryptographicException(
                "The stored secret uses an unauthenticated or unsupported format.");
        }

        var encrypted = Convert.FromBase64String(secretData.EncryptedValue);
        var nonce = Convert.FromBase64String(secretData.IV);
        var authenticationTag = Convert.FromBase64String(secretData.AuthenticationTag);
        var plaintext = new byte[encrypted.Length];

        using var aes = new AesGcm(_encryptionKey, TagSize);
        aes.Decrypt(
            nonce,
            encrypted,
            authenticationTag,
            plaintext,
            AssociatedData(requestedName, secretData));

        return Encoding.UTF8.GetString(plaintext);
    }

    private static byte[] AssociatedData(string requestedName, EncryptedSecretData secretData)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);

        // Protocol domain, not branding. Existing authenticated records bind
        // this exact Noelia 4.x value into their AES-GCM tag.
        WriteAssociatedString(writer, $"Noelia.SecretManager.v{CurrentFormatVersion}");
        WriteAssociatedString(writer, requestedName);
        WriteAssociatedString(writer, secretData.Name);
        writer.Write(secretData.Version);
        writer.Write(secretData.CreatedAt.ToUniversalTime().Ticks);
        writer.Write(secretData.ExpiresAt.HasValue);
        if (secretData.ExpiresAt.HasValue)
        {
            writer.Write(secretData.ExpiresAt.Value.ToUniversalTime().Ticks);
        }
        writer.Write(secretData.IsActive);
        WriteAssociatedString(writer, secretData.CreatedBy);
        writer.Flush();

        return buffer.ToArray();
    }

    private static void WriteAssociatedString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] GenerateEncryptionKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[32]; // 256-bit key
        rng.GetBytes(key);
        return key;
    }

    private static string GenerateSecretValue()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string GetSecretKey(string name) => $"{_keyPrefix}{name}";
    private string GetSecretHistoryKey(string name) => $"{_keyPrefix}history:{name}";
}

/// <summary>
/// Encrypted secret data stored in Redis
/// </summary>
internal class EncryptedSecretData
{
    public int FormatVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public string IV { get; set; } = string.Empty;
    public string AuthenticationTag { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}
