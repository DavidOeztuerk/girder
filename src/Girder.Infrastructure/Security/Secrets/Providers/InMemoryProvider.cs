using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// In-memory secret provider for development/testing
/// </summary>
public class InMemoryProvider : ISecretProvider
{
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, string> _secrets;
    private readonly byte[] _encryptionKey;

    public InMemoryProvider(ILogger logger)
    {
        _logger = logger;
        _secrets = new ConcurrentDictionary<string, string>();
        _encryptionKey = GenerateEncryptionKey();
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_secrets.TryGetValue(key, out var encryptedValue))
        {
            var decrypted = Decrypt(encryptedValue);
            _logger.LogDebug("Secret retrieved from in-memory store: {Key}", key);
            return Task.FromResult<string?>(decrypted);
        }

        return Task.FromResult<string?>(null);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var encrypted = Encrypt(value);
        _secrets[key] = encrypted;
        _logger.LogDebug("Secret stored in in-memory store: {Key}", key);
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(key, out _);
        _logger.LogDebug("Secret deleted from in-memory store: {Key}", key);
        return Task.CompletedTask;
    }

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_secrets.ContainsKey(key));
    }

    public Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<string>>(_secrets.Keys);
    }

    private byte[] GenerateEncryptionKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[32]; // 256-bit key
        rng.GetBytes(key);
        return key;
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