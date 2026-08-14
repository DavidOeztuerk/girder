using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text.Json;

namespace Infrastructure.Security;

/// <summary>
/// Redis-based secure secret manager with encryption
/// </summary>
public class SecretManager : ISecretManager
{
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

            // Development: Generate transient key and log it
            _encryptionKey = GenerateEncryptionKey();
            var base64Key = Convert.ToBase64String(_encryptionKey);
            _logger.LogWarning(
                "No encryption key found in configuration. Generated transient key (DEV ONLY): {Key}. " +
                "IMPORTANT: Set SECRET_MANAGER_ENCRYPTION_KEY_BASE64={KeyValue} for all services to share the same key.",
                base64Key, base64Key);
        }
        else
        {
            _encryptionKey = Convert.FromBase64String(encryptionKeyBase64);
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

            var secretData = JsonSerializer.Deserialize<EncryptedSecretData>(encryptedData!);
            if (secretData == null)
            {
                return null;
            }

            // Check if secret is expired
            if (secretData.ExpiresAt.HasValue && secretData.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning("Attempted to access expired secret: {SecretName}", name);
                return null;
            }

            var decryptedValue = DecryptSecret(secretData.EncryptedValue, secretData.IV);
            
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
            var (encryptedValue, iv) = EncryptSecret(value);
            
            var secretData = new EncryptedSecretData
            {
                Name = name,
                EncryptedValue = encryptedValue,
                IV = iv,
                CreatedAt = DateTime.UtcNow,
                Version = await GetNextVersionAsync(name),
                IsActive = true,
                CreatedBy = "System"
            };

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

    public async Task<IEnumerable<SecretVersion>> GetSecretHistoryAsync(string name, CancellationToken cancellationToken = default)
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
                    var secretData = JsonSerializer.Deserialize<EncryptedSecretData>(item!);
                    if (secretData != null)
                    {
                        versions.Add(new SecretVersion
                        {
                            Name = secretData.Name,
                            Value = "[ENCRYPTED]", // Don't expose actual values
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

    public async Task<IEnumerable<string>> GetSecretNamesAsync(CancellationToken cancellationToken = default)
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
            var secretData = JsonSerializer.Deserialize<EncryptedSecretData>(encryptedData!);
            if (secretData != null)
            {
                secretData.IsActive = false;
                await StoreSecretHistoryAsync(secretData);
            }
        }
    }

    private (string encryptedValue, string iv) EncryptSecret(string value)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor();
        using var msEncrypt = new MemoryStream();
        using var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write);
        using var swEncrypt = new StreamWriter(csEncrypt);
        
        swEncrypt.Write(value);
        swEncrypt.Close();
        
        var encrypted = msEncrypt.ToArray();
        
        return (Convert.ToBase64String(encrypted), Convert.ToBase64String(aes.IV));
    }

    private string DecryptSecret(string encryptedValue, string iv)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedValue);
        var ivBytes = Convert.FromBase64String(iv);
        
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.IV = ivBytes;
        
        using var decryptor = aes.CreateDecryptor();
        using var msDecrypt = new MemoryStream(encryptedBytes);
        using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
        using var srDecrypt = new StreamReader(csDecrypt);
        
        return srDecrypt.ReadToEnd();
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
    public string Name { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public string IV { get; set; } = string.Empty;
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// In-memory secret manager for development/testing
/// </summary>
public class InMemorySecretManager : ISecretManager
{
    private readonly Dictionary<string, List<SecretVersion>> _secrets = new();
    private readonly object _lock = new();
    private readonly ILogger<InMemorySecretManager> _logger;

    public InMemorySecretManager(ILogger<InMemorySecretManager> logger)
    {
        _logger = logger;
    }

    public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_secrets.TryGetValue(name, out var versions))
            {
                var activeVersion = versions.FirstOrDefault(v => v.IsActive);
                if (activeVersion?.ExpiresAt == null || activeVersion.ExpiresAt > DateTime.UtcNow)
                {
                    return Task.FromResult<string?>(activeVersion?.Value);
                }
            }
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetSecretAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (!_secrets.ContainsKey(name))
            {
                _secrets[name] = new List<SecretVersion>();
            }

            // Deactivate existing versions
            foreach (var version in _secrets[name])
            {
                version.IsActive = false;
            }

            var newVersion = new SecretVersion
            {
                Name = name,
                Value = value,
                Version = _secrets[name].Count + 1,
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                CreatedBy = "System"
            };

            _secrets[name].Add(newVersion);
            _logger.LogInformation("Secret set (in-memory): {SecretName}, Version: {Version}", name, newVersion.Version);
        }

        return Task.CompletedTask;
    }

    public Task<string> RotateSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        var newValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return SetSecretAsync(name, newValue, cancellationToken).ContinueWith(_ => newValue, cancellationToken);
    }

    public Task<IEnumerable<SecretVersion>> GetSecretHistoryAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_secrets.TryGetValue(name, out var versions))
            {
                return Task.FromResult(versions.OrderByDescending(v => v.Version).AsEnumerable());
            }
            return Task.FromResult(Enumerable.Empty<SecretVersion>());
        }
    }

    public Task DeleteSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _secrets.Remove(name);
            _logger.LogInformation("Secret deleted (in-memory): {SecretName}", name);
        }
        return Task.CompletedTask;
    }

    public Task<bool> SecretExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_secrets.ContainsKey(name));
        }
    }

    public Task<IEnumerable<string>> GetSecretNamesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_secrets.Keys.AsEnumerable());
        }
    }
}