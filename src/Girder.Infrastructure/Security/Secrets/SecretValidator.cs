using System.Text.RegularExpressions;

namespace Girder.Infrastructure.Security.Secrets;

/// <summary>
/// Validates secret strength and detects placeholder values
/// </summary>
public static partial class SecretValidator
{
    private static readonly HashSet<string> PlaceholderPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "your-secret-key",
        "your-secret",
        "your-password",
        "example",
        "sample",
        "test",
        "demo",
        "placeholder",
        "changeme",
        "password",
        "password123",
        "admin",
        "12345678",
        "localhost",
        "127.0.0.1",
        "default",
        "temp",
        "temporary"
    };

    [GeneratedRegex(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&#])[A-Za-z\d@$!%*?&#]{12,}$")]
    private static partial Regex StrongPasswordRegex();

    [GeneratedRegex(@"^[a-f0-9]{32,}$", RegexOptions.IgnoreCase)]
    private static partial Regex HexKeyRegex();

    [GeneratedRegex(@"^[A-Za-z0-9+/]{43,}={0,2}$")]
    private static partial Regex Base64KeyRegex();

    /// <summary>
    /// Checks if a secret is strong enough
    /// </summary>
    public static bool IsStrongSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        // Check minimum length
        if (secret.Length < 12)
        {
            return false;
        }

        // Check if it's a hex key (32+ chars)
        if (HexKeyRegex().IsMatch(secret))
        {
            return secret.Length >= 32;
        }

        // Check if it's a base64 key
        if (Base64KeyRegex().IsMatch(secret))
        {
            return true;
        }

        // Check password complexity
        return StrongPasswordRegex().IsMatch(secret);
    }

    /// <summary>
    /// Detects placeholder or weak secrets
    /// </summary>
    public static bool IsPlaceholderSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return true;
        }

        var lowerSecret = secret.ToLowerInvariant();

        // Check exact matches
        if (PlaceholderPatterns.Contains(lowerSecret))
        {
            return true;
        }

        // Check if contains placeholder patterns
        foreach (var pattern in PlaceholderPatterns)
        {
            if (lowerSecret.Contains(pattern))
            {
                return true;
            }
        }

        // Check for sequential patterns
        if (ContainsSequentialPattern(secret))
        {
            return true;
        }

        // Check for repeated characters
        if (ContainsRepeatedPattern(secret))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Validates a JWT secret
    /// </summary>
    public static bool IsValidJwtSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        // JWT secrets should be at least 256 bits (32 bytes)
        if (secret.Length < 32)
        {
            return false;
        }

        // Check if it's not a placeholder
        if (IsPlaceholderSecret(secret))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates a database connection string
    /// </summary>
    public static ValidationResult ValidateConnectionString(string connectionString)
    {
        var result = new ValidationResult { IsValid = true };

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            result.IsValid = false;
            result.Errors.Add("Connection string is empty");
            return result;
        }

        // Check for weak passwords in connection string
        if (connectionString.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            var passwordMatch = Regex.Match(connectionString, @"password=([^;]+)", RegexOptions.IgnoreCase);
            if (passwordMatch.Success)
            {
                var password = passwordMatch.Groups[1].Value;
                if (IsPlaceholderSecret(password))
                {
                    result.Warnings.Add("Connection string contains a weak or placeholder password");
                }
            }
        }

        // Check for localhost/development servers
        if (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            connectionString.Contains("127.0.0.1"))
        {
            result.Warnings.Add("Connection string points to localhost");
        }

        // Check for default ports
        if (connectionString.Contains(":1433") || // SQL Server default
            connectionString.Contains(":5432") || // PostgreSQL default
            connectionString.Contains(":3306") || // MySQL default
            connectionString.Contains(":6379"))   // Redis default
        {
            result.Warnings.Add("Connection string uses default port");
        }

        return result;
    }

    private static bool ContainsSequentialPattern(string secret)
    {
        var sequences = new[]
        {
            "0123456789",
            "9876543210",
            "abcdefghij",
            "qwertyuiop"
        };

        var lowerSecret = secret.ToLowerInvariant();
        var minimumSequenceLength = secret.Length >= 32 ? 8 : 4;

        return sequences.Any(seq => lowerSecret.Contains(seq.Substring(0, Math.Min(minimumSequenceLength, seq.Length))));
    }

    private static bool ContainsRepeatedPattern(string secret)
    {
        if (secret.Length < 4)
        {
            return false;
        }

        // Check for repeated characters (e.g., "aaaa", "1111"). Long random keys can
        // naturally contain short runs, so require a longer run for high-entropy values.
        var minimumRunLength = secret.Length >= 32 ? 8 : 4;

        for (int i = 0; i <= secret.Length - minimumRunLength; i++)
        {
            if (secret.Skip(i).Take(minimumRunLength).Distinct().Count() == 1)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Validation result with warnings and errors
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
}
