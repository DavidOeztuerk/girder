using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// Secret provider using ASP.NET Core Data Protection API
/// </summary>
public class DataProtectionSecretProvider : ISecretProvider
{
    private readonly ILogger<DataProtectionSecretProvider> _logger;
    private readonly IDataProtector _protector;
    private readonly ConcurrentDictionary<string, string> _secrets;

    public DataProtectionSecretProvider(
        ILogger<DataProtectionSecretProvider> logger,
        IDataProtectionProvider dataProtectionProvider)
    {
        _logger = logger;
        _protector = dataProtectionProvider.CreateProtector("Skillswap.Secrets");
        _secrets = new ConcurrentDictionary<string, string>();
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_secrets.TryGetValue(key, out var encryptedValue))
        {
            try
            {
                var decrypted = _protector.Unprotect(encryptedValue);
                return Task.FromResult<string?>(decrypted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt secret: {Key}", key);
                return Task.FromResult<string?>(null);
            }
        }
        return Task.FromResult<string?>(null);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var encrypted = _protector.Protect(value);
        _secrets[key] = encrypted;
        _logger.LogDebug("Secret stored with Data Protection: {Key}", key);
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(key, out _);
        _logger.LogDebug("Secret deleted: {Key}", key);
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

    public async Task<string> GetRawEncryptedAsync(string key)
    {
        if (_secrets.TryGetValue(key, out var encrypted))
        {
            return await Task.FromResult(encrypted);
        }
        return await Task.FromResult(string.Empty);
    }
}