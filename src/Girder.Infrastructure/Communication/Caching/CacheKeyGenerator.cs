using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Communication.Caching;

/// <summary>
/// Generates cache keys for service requests
/// </summary>
public static class CacheKeyGenerator
{
    /// <summary>
    /// Generate cache key for a GET request
    /// </summary>
    public static string GenerateKey(string serviceName, string endpoint, Dictionary<string, string>? headers = null)
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"svc:{serviceName}:{endpoint}");

        if (headers != null && headers.Any())
        {
            var sortedHeaders = headers
                .Where(h => IsCacheableHeader(h.Key))
                .OrderBy(h => h.Key)
                .ToList();

            if (sortedHeaders.Any())
            {
                keyBuilder.Append(":");
                keyBuilder.Append(JsonSerializer.Serialize(sortedHeaders));
            }
        }

        var key = keyBuilder.ToString();

        // If key is too long, hash it
        if (key.Length > 250)
        {
            return $"svc:{serviceName}:{ComputeHash(key)}";
        }

        return key;
    }

    /// <summary>
    /// Generate cache key for a POST request with body
    /// </summary>
    public static string GenerateKey<TRequest>(
        string serviceName,
        string endpoint,
        TRequest request,
        Dictionary<string, string>? headers = null) where TRequest : class
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"svc:{serviceName}:{endpoint}");

        // Add request body to key
        var requestJson = JsonSerializer.Serialize(request);
        keyBuilder.Append($":{ComputeHash(requestJson)}");

        if (headers != null && headers.Any())
        {
            var sortedHeaders = headers
                .Where(h => IsCacheableHeader(h.Key))
                .OrderBy(h => h.Key)
                .ToList();

            if (sortedHeaders.Any())
            {
                keyBuilder.Append(":");
                keyBuilder.Append(ComputeHash(JsonSerializer.Serialize(sortedHeaders)));
            }
        }

        var key = keyBuilder.ToString();

        // If key is too long, hash it
        if (key.Length > 250)
        {
            return $"svc:{serviceName}:{ComputeHash(key)}";
        }

        return key;
    }

    /// <summary>
    /// Determine if a header should be included in the cache key
    /// </summary>
    private static bool IsCacheableHeader(string headerName)
    {
        // Exclude non-cacheable headers
        var nonCacheableHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Cookie",
            "Set-Cookie",
            "X-Request-ID",
            "User-Agent",
            "Date",
            "Age",
            "Expires"
        };

        return !nonCacheableHeaders.Contains(headerName);
    }

    /// <summary>
    /// Compute SHA256 hash of a string
    /// </summary>
    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
