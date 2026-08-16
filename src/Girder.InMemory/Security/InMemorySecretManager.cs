using Girder.Abstractions.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;
using Girder.Abstractions.Security;

namespace Girder.InMemory.Security;

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