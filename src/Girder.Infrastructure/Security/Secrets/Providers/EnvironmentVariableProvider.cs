using Microsoft.Extensions.Logging;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// Environment variable secret provider
/// </summary>
public class EnvironmentVariableProvider : ISecretProvider
{
    private readonly ILogger _logger;
    private readonly string _prefix;

    public EnvironmentVariableProvider(ILogger logger, string prefix = "SKILLSWAP_")
    {
        _logger = logger;
        _prefix = prefix;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var envKey = GetEnvironmentKey(key);
        var value = Environment.GetEnvironmentVariable(envKey);
        
        if (!string.IsNullOrEmpty(value))
        {
            _logger.LogDebug("Secret retrieved from environment variable: {Key}", envKey);
        }
        
        return Task.FromResult(value);
    }

    public Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var envKey = GetEnvironmentKey(key);
        Environment.SetEnvironmentVariable(envKey, value);
        _logger.LogDebug("Secret set in environment variable: {Key}", envKey);
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var envKey = GetEnvironmentKey(key);
        Environment.SetEnvironmentVariable(envKey, null);
        _logger.LogDebug("Secret deleted from environment variable: {Key}", envKey);
        return Task.CompletedTask;
    }

    public Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var envKey = GetEnvironmentKey(key);
        var exists = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envKey));
        return Task.FromResult(exists);
    }

    public Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        var allVars = Environment.GetEnvironmentVariables();
        var secretKeys = new List<string>();
        
        foreach (System.Collections.DictionaryEntry entry in allVars)
        {
            var key = entry.Key?.ToString();
            if (key != null && key.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase))
            {
                secretKeys.Add(key.Substring(_prefix.Length));
            }
        }
        
        return Task.FromResult<IEnumerable<string>>(secretKeys);
    }

    private string GetEnvironmentKey(string key)
    {
        // Convert to uppercase and replace special characters
        var envKey = $"{_prefix}{key}".ToUpperInvariant()
            .Replace(":", "_")
            .Replace(".", "_")
            .Replace("-", "_");
        
        return envKey;
    }
}