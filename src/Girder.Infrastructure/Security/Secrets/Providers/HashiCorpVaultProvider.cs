using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Security.Secrets;

/// <summary>
/// HashiCorp Vault secret provider
/// To use: Install-Package VaultSharp for production implementation
/// </summary>
public class HashiCorpVaultProvider : IVersionedSecretProvider
{
    private readonly ILogger _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly VaultConfiguration _vaultConfig;

    public HashiCorpVaultProvider(ILogger logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = new HttpClient();
        _vaultConfig = LoadConfiguration();
        ConfigureHttpClient();
    }

    private VaultConfiguration LoadConfiguration()
    {
        return new VaultConfiguration
        {
            Address = _configuration["Vault:Address"] ?? "http://localhost:8200",
            Token = _configuration["Vault:Token"] ?? throw new InvalidOperationException("Vault token is required"),
            MountPoint = _configuration["Vault:MountPoint"] ?? "secret",
            Namespace = _configuration["Vault:Namespace"],
            ApiVersion = _configuration["Vault:ApiVersion"] ?? "v2"
        };
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_vaultConfig.Address);
        _httpClient.DefaultRequestHeaders.Add("X-Vault-Token", _vaultConfig.Token);
        
        if (!string.IsNullOrEmpty(_vaultConfig.Namespace))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Vault-Namespace", _vaultConfig.Namespace);
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetSecretPath(key);
            var response = await _httpClient.GetAsync(path, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                
                _logger.LogError("Failed to get secret from Vault: {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var vaultResponse = JsonSerializer.Deserialize<VaultResponse>(content);
            
            if (vaultResponse?.Data?.Data?.TryGetValue("value", out var secretValue) == true)
            {
                return secretValue?.ToString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret from Vault: {Key}", key);
            throw;
        }
    }

    public async Task SetSecretAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetSecretPath(key);
            var payload = new
            {
                data = new Dictionary<string, string>
                {
                    ["value"] = value
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(path, content, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to set secret in Vault: {StatusCode}", response.StatusCode);
                throw new InvalidOperationException($"Failed to set secret in Vault: {response.StatusCode}");
            }

            _logger.LogInformation("Secret stored in Vault: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting secret in Vault: {Key}", key);
            throw;
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = GetSecretPath(key);
            var response = await _httpClient.DeleteAsync(path, cancellationToken);
            
            if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogError("Failed to delete secret from Vault: {StatusCode}", response.StatusCode);
                throw new InvalidOperationException($"Failed to delete secret from Vault: {response.StatusCode}");
            }

            _logger.LogInformation("Secret deleted from Vault: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting secret from Vault: {Key}", key);
            throw;
        }
    }

    public async Task<bool> SecretExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var secret = await GetSecretAsync(key, cancellationToken);
        return secret != null;
    }

    public async Task<IEnumerable<string>> ListSecretKeysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"/v1/{_vaultConfig.MountPoint}/metadata";
            var response = await _httpClient.GetAsync($"{path}?list=true", cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to list secrets from Vault: {StatusCode}", response.StatusCode);
                return Enumerable.Empty<string>();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var vaultResponse = JsonSerializer.Deserialize<VaultListResponse>(content);
            
            return vaultResponse?.Data?.Keys ?? Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secrets from Vault");
            return Enumerable.Empty<string>();
        }
    }

    public async Task<string?> GetSecretVersionAsync(string key, string version, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"/v1/{_vaultConfig.MountPoint}/data/{key}?version={version}";
            var response = await _httpClient.GetAsync(path, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var vaultResponse = JsonSerializer.Deserialize<VaultResponse>(content);
            
            if (vaultResponse?.Data?.Data?.TryGetValue("value", out var secretValue) == true)
            {
                return secretValue?.ToString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret version from Vault: {Key}, Version: {Version}", key, version);
            return null;
        }
    }

    public async Task<IEnumerable<SecretVersion>> ListSecretVersionsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = $"/v1/{_vaultConfig.MountPoint}/metadata/{key}";
            var response = await _httpClient.GetAsync(path, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return Enumerable.Empty<SecretVersion>();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var vaultResponse = JsonSerializer.Deserialize<VaultMetadataResponse>(content);
            
            if (vaultResponse?.Data?.Versions == null)
            {
                return Enumerable.Empty<SecretVersion>();
            }

            return vaultResponse.Data.Versions
                .Select(v => new SecretVersion
                {
                    Name = key,
                    Value = string.Empty, // Not returned in metadata
                    Version = v.Value.Version,
                    CreatedAt = DateTime.Parse(v.Value.CreatedTime),
                    ExpiresAt = v.Value.DeletionTime != null ? DateTime.Parse(v.Value.DeletionTime) : null,
                    IsActive = v.Value.Version == vaultResponse.Data.CurrentVersion,
                    CreatedBy = "Vault"
                })
                .OrderByDescending(v => v.CreatedAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing secret versions from Vault: {Key}", key);
            return Enumerable.Empty<SecretVersion>();
        }
    }

    private string GetSecretPath(string key)
    {
        return _vaultConfig.ApiVersion == "v2" 
            ? $"/v1/{_vaultConfig.MountPoint}/data/{key}"
            : $"/v1/{_vaultConfig.MountPoint}/{key}";
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

internal class VaultConfiguration
{
    public string Address { get; set; } = "http://localhost:8200";
    public string Token { get; set; } = string.Empty;
    public string MountPoint { get; set; } = "secret";
    public string? Namespace { get; set; }
    public string ApiVersion { get; set; } = "v2";
}

internal class VaultResponse
{
    public VaultData? Data { get; set; }
}

internal class VaultData
{
    public Dictionary<string, object>? Data { get; set; }
    public VaultMetadata? Metadata { get; set; }
}

internal class VaultMetadata
{
    public DateTime CreatedTime { get; set; }
    public string? DeletionTime { get; set; }
    public int Version { get; set; }
}

internal class VaultListResponse
{
    public VaultListData? Data { get; set; }
}

internal class VaultListData
{
    public List<string>? Keys { get; set; }
}

internal class VaultMetadataResponse
{
    public VaultMetadataData? Data { get; set; }
}

internal class VaultMetadataData
{
    public int CurrentVersion { get; set; }
    public Dictionary<string, VaultVersionInfo>? Versions { get; set; }
}

internal class VaultVersionInfo
{
    public string CreatedTime { get; set; } = string.Empty;
    public string? DeletionTime { get; set; }
    public int Version { get; set; }
}