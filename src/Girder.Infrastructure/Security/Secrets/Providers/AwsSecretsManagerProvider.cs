using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// AWS Secrets Manager provider
/// To use: Install-Package AWSSDK.SecretsManager
/// </summary>
public class AwsSecretsManagerProvider : ISecretProvider
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly Dictionary<string, string> _inMemoryCache = new();

    public AwsSecretsManagerProvider(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        // In production, initialize AWS Secrets Manager client here
        _logger.LogWarning("AWS Secrets Manager provider is running in stub mode. Install AWSSDK.SecretsManager for production use.");
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_inMemoryCache.TryGetValue(key, out var value))
        {
            return Task.FromResult<string?>(value);
        }
        return Task.FromResult<string?>(null);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _inMemoryCache[key] = value;
        _logger.LogInformation("Secret stored in AWS Secrets Manager stub: {Key}", key);
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        _inMemoryCache.Remove(key);
        return Task.CompletedTask;
    }

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_inMemoryCache.ContainsKey(key));
    }

    public Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<string>>(_inMemoryCache.Keys);
    }
}