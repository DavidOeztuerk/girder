using Noelia.Abstractions.Security.Secrets;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Noelia.InMemory.Security;

public class InMemorySecretManager : IVersionedSecretProvider
{
    private readonly Dictionary<string, List<StoredSecretVersion>> _secrets = new();
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
                _secrets[name] = new List<StoredSecretVersion>();
            }

            // Deactivate existing versions
            foreach (var version in _secrets[name])
            {
                version.IsActive = false;
            }

            var newVersion = new StoredSecretVersion
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

    public Task<string?> GetSecretVersionAsync(
        string key,
        string version,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(version, out var versionNumber))
        {
            return Task.FromResult<string?>(null);
        }

        lock (_lock)
        {
            var value = _secrets.TryGetValue(key, out var versions)
                ? versions.FirstOrDefault(item => item.Version == versionNumber)?.Value
                : null;
            return Task.FromResult(value);
        }
    }

    public Task<IEnumerable<SecretVersion>> ListSecretVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_secrets.TryGetValue(name, out var versions))
            {
                var metadata = versions
                    .OrderByDescending(version => version.Version)
                    .Select(version => new SecretVersion
                    {
                        Name = version.Name,
                        Version = version.Version,
                        CreatedAt = version.CreatedAt,
                        ExpiresAt = version.ExpiresAt,
                        IsActive = version.IsActive,
                        CreatedBy = version.CreatedBy
                    })
                    .ToArray();
                return Task.FromResult<IEnumerable<SecretVersion>>(metadata);
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

    public Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_secrets.Keys.AsEnumerable());
        }
    }

    private sealed class StoredSecretVersion
    {
        public string Name { get; init; } = string.Empty;
        public string Value { get; init; } = string.Empty;
        public int Version { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ExpiresAt { get; init; }
        public bool IsActive { get; set; }
        public string CreatedBy { get; init; } = "System";
    }
}
