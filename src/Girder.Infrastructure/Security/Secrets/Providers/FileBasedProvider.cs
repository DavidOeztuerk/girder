using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Girder.Infrastructure.Security.Secrets;

/// <summary>
/// Secret provider that keeps secrets in an encrypted file on disk.
/// </summary>
/// <remarks>
/// <para>
/// Values are sealed with AES-GCM under a key derived from
/// <c>Secrets:MasterPassword</c> and a salt generated once per installation and
/// stored beside the secrets file. A salt is not secret; its job is to make one
/// precomputation useless against every other installation.
/// </para>
/// <para>
/// There is no fallback password. A secret store that encrypts under a value
/// compiled into the library protects nothing.
/// </para>
/// </remarks>
public class FileBasedProvider : ISecretProvider
{
    /// <summary>
    /// OWASP guidance for PBKDF2-HMAC-SHA256. Raising this invalidates existing
    /// files, so it belongs with a format version rather than a quiet bump.
    /// </summary>
    private const int Pbkdf2Iterations = 600_000;

    private const int SaltSizeBytes = 32;
    private const int KeySizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly string _secretsFilePath;
    private readonly string _saltFilePath;
    private readonly byte[] _encryptionKey;
    private Dictionary<string, string> _secrets = new();

    /// <exception cref="InvalidOperationException">No master password is configured.</exception>
    public FileBasedProvider(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _secretsFilePath = configuration["Secrets:FilePath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), "secrets.encrypted");
        _saltFilePath = _secretsFilePath + ".salt";

        var password = configuration["Secrets:MasterPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Secrets:MasterPassword is required. This provider will not fall back to a "
                + "built-in password, because a secret encrypted under a value from the library "
                + "source is not encrypted.");
        }

        _encryptionKey = DeriveKey(password, LoadOrCreateSalt());

        LoadSecrets();
    }

    /// <summary>
    /// Reads the installation's salt, generating and storing one on first use.
    /// </summary>
    private byte[] LoadOrCreateSalt()
    {
        if (File.Exists(_saltFilePath))
        {
            return Convert.FromBase64String(File.ReadAllText(_saltFilePath));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

        var directory = Path.GetDirectoryName(_saltFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_saltFilePath, Convert.ToBase64String(salt));
        _logger.LogInformation("Generated a new secrets salt at {Path}", _saltFilePath);

        return salt;
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySizeBytes);

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


    /// <summary>
    /// Seals a value with AES-GCM. Layout: nonce, tag, ciphertext.
    /// </summary>
    /// <remarks>
    /// GCM authenticates as well as encrypts, so a tampered file fails to open
    /// instead of decrypting to something the caller then trusts.
    /// </remarks>
    private string Encrypt(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_encryptionKey, TagSizeBytes);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        var result = new byte[NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSizeBytes);
        cipherBytes.CopyTo(result, NonceSizeBytes + TagSizeBytes);

        return Convert.ToBase64String(result);
    }

    /// <exception cref="CryptographicException">
    /// The value was altered, or the key does not match.
    /// </exception>
    private string Decrypt(string cipherText)
    {
        var buffer = Convert.FromBase64String(cipherText);

        if (buffer.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("The stored value is too short to be a sealed secret.");
        }

        var nonce = buffer.AsSpan(0, NonceSizeBytes);
        var tag = buffer.AsSpan(NonceSizeBytes, TagSizeBytes);
        var cipher = buffer.AsSpan(NonceSizeBytes + TagSizeBytes);
        var plainBytes = new byte[cipher.Length];

        using var aes = new AesGcm(_encryptionKey, TagSizeBytes);
        aes.Decrypt(nonce, cipher, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}