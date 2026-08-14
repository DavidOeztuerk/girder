using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// Secure secret manager with multiple provider support and caching
/// </summary>
public class SecureSecretManager : ISecretManager
{
    private readonly ILogger<SecureSecretManager> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly IMemoryCache _cache;
    private readonly ISecretProvider _provider;
    private readonly SecretManagerOptions _options;
    private readonly ConcurrentDictionary<string, SecretHistory> _secretHistory;

    public SecureSecretManager(
        ILogger<SecureSecretManager> logger,
        IConfiguration configuration,
        IHostEnvironment environment,
        IMemoryCache? cache = null)
    {
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
        _secretHistory = new ConcurrentDictionary<string, SecretHistory>();
        
        _options = LoadOptions();
        _provider = CreateProvider();
        
        ValidateConfiguration();
    }

    private SecretManagerOptions LoadOptions()
    {
        return new SecretManagerOptions
        {
            Provider = _configuration["Secrets:Provider"] ?? "InMemory",
            CacheEnabled = _configuration.GetValue<bool>("Secrets:CacheEnabled", true),
            CacheDurationSeconds = _configuration.GetValue<int>("Secrets:CacheDurationSeconds", 300),
            EncryptionEnabled = _configuration.GetValue<bool>("Secrets:EncryptionEnabled", true),
            AuditEnabled = _configuration.GetValue<bool>("Secrets:AuditEnabled", true)
        };
    }

    private ISecretProvider CreateProvider()
    {
        return _options.Provider.ToLowerInvariant() switch
        {
            "vault" or "hashicorp" => new HashiCorpVaultProvider(_logger, _configuration),
            "azure" or "azurekeyvault" => new AzureKeyVaultProvider(_logger, _configuration),
            "aws" or "awssecretsmanager" => new AwsSecretsManagerProvider(_logger, _configuration),
            "environment" or "env" => new EnvironmentVariableProvider(_logger),
            "file" => new FileBasedProvider(_logger, _configuration),
            "inmemory" or "memory" => new InMemoryProvider(_logger),
            _ => _environment.IsDevelopment() 
                ? new InMemoryProvider(_logger) 
                : throw new InvalidOperationException($"Unknown secret provider: {_options.Provider}")
        };
    }

    private void ValidateConfiguration()
    {
        // Check for hardcoded secrets in configuration
        var suspiciousKeys = new[]
        {
            "REPLACE_WITH_SECURE_SECRET_IN_PRODUCTION",
            "SecretManager:EncryptionKey",
            "SecretManager:EncryptionKeyBase64",
            "JwtSettings:Secret",
            "Jwt:Secret",
            "ConnectionStrings:DefaultConnection",
            "Redis:Password",
            "RabbitMQ:Password",
            "Smtp:Password"
        };

        foreach (var key in suspiciousKeys)
        {
            var value = _configuration[key];
            if (!string.IsNullOrEmpty(value) && SecretValidator.IsPlaceholderSecret(value))
            {
                _logger.LogWarning("Potential placeholder secret detected in configuration: {Key}", key);
            }
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Secret key cannot be empty", nameof(key));
        }

        // Check cache first
        if (_options.CacheEnabled && _cache.TryGetValue<string>($"secret:{key}", out var cachedValue))
        {
            _logger.LogDebug("Secret retrieved from cache: {Key}", key);
            return cachedValue;
        }

        // Get from provider
        var secret = await _provider.GetSecretAsync(key, cancellationToken);
        
        if (secret != null && _options.CacheEnabled)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.CacheDurationSeconds)
            };
            _cache.Set($"secret:{key}", secret, cacheOptions);
        }

        if (_options.AuditEnabled)
        {
            LogSecretAccess(key, secret != null);
        }

        return secret;
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Secret key cannot be empty", nameof(key));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Secret value cannot be empty", nameof(value));
        }

        // Validate secret strength
        if (SecretValidator.IsPlaceholderSecret(value))
        {
            _logger.LogWarning("Attempting to set a placeholder secret for key: {Key}", key);
            
            if (_environment.IsProduction())
            {
                throw new InvalidOperationException("Cannot use placeholder secrets in production");
            }
        }

        if (!SecretValidator.IsStrongSecret(value) && key.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Weak password detected for key: {Key}", key);
        }

        // Store history
        await StoreSecretHistory(key, value);

        // Set in provider
        await _provider.SetSecretAsync(key, value, cancellationToken);

        // Invalidate cache
        if (_options.CacheEnabled)
        {
            _cache.Remove($"secret:{key}");
        }

        if (_options.AuditEnabled)
        {
            LogSecretModification(key, "SET");
        }

        _logger.LogInformation("Secret set successfully: {Key}", key);
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Secret key cannot be empty", nameof(key));
        }

        await _provider.DeleteSecretAsync(key, cancellationToken);

        // Invalidate cache
        if (_options.CacheEnabled)
        {
            _cache.Remove($"secret:{key}");
        }

        // Remove history
        _secretHistory.TryRemove(key, out _);

        if (_options.AuditEnabled)
        {
            LogSecretModification(key, "DELETE");
        }

        _logger.LogInformation("Secret deleted: {Key}", key);
    }

    public async Task<string> RotateSecretAsync(string name, CancellationToken cancellationToken = default)
    {
        var oldValue = await GetSecretAsync(name, cancellationToken);
        
        if (oldValue == null)
        {
            throw new InvalidOperationException($"Cannot rotate non-existent secret: {name}");
        }

        // Generate new secret
        var newValue = SecretGenerator.GenerateSecret(32, SecretType.AlphanumericWithSpecial);
        
        await SetSecretAsync(name, newValue, cancellationToken);

        if (_options.AuditEnabled)
        {
            LogSecretModification(name, "ROTATE");
        }

        _logger.LogInformation("Secret rotated successfully: {Key}", name);
        
        return newValue;
    }

    public async Task<IEnumerable<SecretVersion>> GetSecretHistoryAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_secretHistory.TryGetValue(name, out var history))
        {
            var versions = history.Entries.Select((entry, index) => new SecretVersion
            {
                Name = name,
                Value = entry.Value,
                Version = history.Entries.Count - index,
                CreatedAt = entry.Timestamp,
                IsActive = index == 0,
                CreatedBy = entry.Environment
            });
            
            return await Task.FromResult(versions.OrderByDescending(v => v.Version));
        }

        return await Task.FromResult(Enumerable.Empty<SecretVersion>());
    }

    public async Task<bool> SecretExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _provider.SecretExistsAsync(name, cancellationToken);
    }

    public async Task<IEnumerable<string>> GetSecretNamesAsync(CancellationToken cancellationToken = default)
    {
        return await _provider.ListSecretKeysAsync(cancellationToken);
    }

    public async Task<string?> GetRawSecretAsync(string key)
    {
        // For testing purposes - gets the raw encrypted value
        return await _provider.GetSecretAsync(key);
    }

    private async Task StoreSecretHistory(string key, string value)
    {
        var history = _secretHistory.GetOrAdd(key, _ => new SecretHistory());
        
        history.Entries.Add(new SecretHistoryEntry(
            value,
            DateTime.UtcNow,
            _environment.EnvironmentName));

        // Keep only last 10 versions
        if (history.Entries.Count > 10)
        {
            history.Entries.RemoveAt(0);
        }

        await Task.CompletedTask;
    }

    private void LogSecretAccess(string key, bool found)
    {
        _logger.LogInformation("Secret access: Key={Key}, Found={Found}, Environment={Environment}",
            key, found, _environment.EnvironmentName);
    }

    private void LogSecretModification(string key, string operation)
    {
        _logger.LogInformation("Secret modification: Key={Key}, Operation={Operation}, Environment={Environment}",
            key, operation, _environment.EnvironmentName);
    }
}

/// <summary>
/// Secret manager options
/// </summary>
public class SecretManagerOptions
{
    public string Provider { get; set; } = "InMemory";
    public bool CacheEnabled { get; set; } = true;
    public int CacheDurationSeconds { get; set; } = 300;
    public bool EncryptionEnabled { get; set; } = true;
    public bool AuditEnabled { get; set; } = true;
}

/// <summary>
/// Secret history tracking
/// </summary>
public class SecretHistory
{
    public List<SecretHistoryEntry> Entries { get; } = new();
}

/// <summary>
/// Secret history entry
/// </summary>
public record SecretHistoryEntry(
    string Value,
    DateTime Timestamp,
    string Environment);