using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// File-based secret provider for development/testing
/// </summary>
public class FileBasedProvider : ISecretProvider
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly string _secretsFilePath;
    private readonly byte[] _encryptionKey;
    private Dictionary<string, string> _secrets = new();

    public FileBasedProvider(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _secretsFilePath = configuration["Secrets:FilePath"] ?? Path.Combine(Directory.GetCurrentDirectory(), "secrets.encrypted");
        _encryptionKey = DeriveKeyFromPassword(configuration["Secrets:MasterPassword"] ?? "DefaultDevelopmentPassword123!");
        
        LoadSecrets();
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_secrets.TryGetValue(key, out var encryptedValue))
        {
            var decrypted = Decrypt(encryptedValue);
            return Task.FromResult<string?>(decrypted);
        }
        return Task.FromResult<string?>(null);
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var encrypted = Encrypt(value);
        _secrets[key] = encrypted;
        await SaveSecretsAsync();
        _logger.LogInformation("Secret stored in file: {Key}", key);
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_secrets.Remove(key))
        {
            await SaveSecretsAsync();
            _logger.LogInformation("Secret deleted from file: {Key}", key);
        }
    }

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_secrets.ContainsKey(key));
    }

    public Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<string>>(_secrets.Keys);
    }

    private void LoadSecrets()
    {
        if (File.Exists(_secretsFilePath))
        {
            try
            {
                var encryptedContent = File.ReadAllText(_secretsFilePath);
                var decryptedContent = Decrypt(encryptedContent);
                _secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(decryptedContent) ?? new();
                _logger.LogInformation("Loaded {Count} secrets from file", _secrets.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load secrets from file");
                _secrets = new Dictionary<string, string>();
            }
        }
    }

    private async Task SaveSecretsAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_secrets);
            var encrypted = Encrypt(json);
            await File.WriteAllTextAsync(_secretsFilePath, encrypted);
            
            // Set file permissions (Unix/Linux only)
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(_secretsFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save secrets to file");
            throw;
        }
    }

    private byte[] DeriveKeyFromPassword(string password)
    {
        using var deriveBytes = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes("SkillswapSalt"), 10000, HashAlgorithmName.SHA256);
        return deriveBytes.GetBytes(32);
    }

    private string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    private string Decrypt(string cipherText)
    {
        var buffer = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        var iv = new byte[aes.IV.Length];
        var cipher = new byte[buffer.Length - iv.Length];

        Array.Copy(buffer, 0, iv, 0, iv.Length);
        Array.Copy(buffer, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}