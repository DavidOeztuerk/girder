using Infrastructure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Configuration;

/// <summary>
/// Validator for JWT configuration
/// </summary>
public class JwtConfigurationValidator : IConfigurationValidator
{
    private readonly IConfiguration _configuration;

    public JwtConfigurationValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string SectionName => "Jwt";
    public int Priority => 100;

    public ConfigurationValidationResult Validate()
    {
        var result = new ConfigurationValidationResult { SectionName = SectionName };
        var jwtSection = _configuration.GetSection(SectionName);

        // Check if section exists
        if (!jwtSection.Exists())
        {
            result.AddError(SectionName, "JWT configuration section is missing", "Add a 'Jwt' section to your configuration");
            return result;
        }

        // Validate Secret
        var secret = jwtSection["Secret"];
        if (string.IsNullOrEmpty(secret))
        {
            result.AddError("Jwt:Secret", "JWT Secret is required", "Set a strong secret key for JWT token signing");
        }
        else if (secret.Length < 32)
        {
            result.AddError("Jwt:Secret", "JWT Secret is too short (minimum 32 characters)", "Use a longer, more secure secret key");
        }
        else if (secret == "your-secret-key" || secret.Contains("example") || secret.Contains("sample"))
        {
            result.AddError("Jwt:Secret", "JWT Secret appears to be a placeholder value", "Replace with a production-ready secret");
        }

        // Validate Issuer
        var issuer = jwtSection["Issuer"];
        if (string.IsNullOrEmpty(issuer))
        {
            result.AddError("Jwt:Issuer", "JWT Issuer is required", "Set the JWT issuer to your application name");
        }

        // Validate Audience
        var audience = jwtSection["Audience"];
        if (string.IsNullOrEmpty(audience))
        {
            result.AddError("Jwt:Audience", "JWT Audience is required", "Set the JWT audience to your application name");
        }

        // Validate ExpirationInMinutes
        var expirationStr = jwtSection["ExpirationInMinutes"];
        if (!string.IsNullOrEmpty(expirationStr))
        {
            if (!int.TryParse(expirationStr, out var expiration))
            {
                result.AddError("Jwt:ExpirationInMinutes", "JWT ExpirationInMinutes must be a valid integer", "Use a numeric value in minutes");
            }
            else if (expiration <= 0)
            {
                result.AddError("Jwt:ExpirationInMinutes", "JWT ExpirationInMinutes must be greater than 0", "Set a positive expiration time");
            }
            else if (expiration > 43200) // 30 days
            {
                result.AddWarning("Jwt:ExpirationInMinutes", "JWT expiration is very long (>30 days)", "Consider using a shorter expiration time for security");
            }
        }

        return result;
    }
}

/// <summary>
/// Validator for database configuration
/// </summary>
public class DatabaseConfigurationValidator : IConfigurationValidator
{
    private readonly IConfiguration _configuration;

    public DatabaseConfigurationValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string SectionName => "ConnectionStrings";
    public int Priority => 90;

    public ConfigurationValidationResult Validate()
    {
        var result = new ConfigurationValidationResult { SectionName = SectionName };
        var connectionStringsSection = _configuration.GetSection(SectionName);

        if (!connectionStringsSection.Exists())
        {
            result.AddError(SectionName, "ConnectionStrings section is missing", "Add a ConnectionStrings section with database connection strings");
            return result;
        }

        // Validate DefaultConnection
        var defaultConnection = connectionStringsSection["DefaultConnection"];
        if (string.IsNullOrEmpty(defaultConnection))
        {
            result.AddError("ConnectionStrings:DefaultConnection", "Default database connection string is missing", "Add a DefaultConnection string");
        }
        else
        {
            ValidateConnectionString(defaultConnection, "DefaultConnection", result);
        }

        // Validate Redis connection
        var redisConnection = connectionStringsSection["Redis"];
        if (!string.IsNullOrEmpty(redisConnection))
        {
            ValidateRedisConnectionString(redisConnection, result);
        }

        return result;
    }

    private void ValidateConnectionString(string connectionString, string keyName, ConfigurationValidationResult result)
    {
        // Check for common insecure patterns
        if (connectionString.Contains("password=", StringComparison.OrdinalIgnoreCase) && 
            (connectionString.Contains("password=123") || connectionString.Contains("password=admin")))
        {
            result.AddWarning($"ConnectionStrings:{keyName}", "Connection string contains a weak password", "Use a strong database password");
        }

        // Check for localhost in production-like environments
        if (connectionString.Contains("localhost") || connectionString.Contains("127.0.0.1"))
        {
            var environment = _configuration["ASPNETCORE_ENVIRONMENT"];
            if (environment != null && !environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                result.AddWarning($"ConnectionStrings:{keyName}", "Connection string uses localhost in non-development environment", "Use proper database server addresses in production");
            }
        }

        // Check for SQL Server integrated security
        if (connectionString.Contains("Integrated Security=true", StringComparison.OrdinalIgnoreCase))
        {
            result.AddWarning($"ConnectionStrings:{keyName}", "Using integrated security", "Ensure the application pool identity has database access");
        }
    }

    private void ValidateRedisConnectionString(string redisConnection, ConfigurationValidationResult result)
    {
        if (redisConnection.Contains("localhost") || redisConnection.Contains("127.0.0.1"))
        {
            var environment = _configuration["ASPNETCORE_ENVIRONMENT"];
            if (environment != null && !environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
            {
                result.AddWarning("ConnectionStrings:Redis", "Redis connection uses localhost in non-development environment", "Use proper Redis server addresses in production");
            }
        }

        // Check if password is specified for Redis
        if (!redisConnection.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            result.AddWarning("ConnectionStrings:Redis", "Redis connection string does not specify a password", "Consider using password authentication for Redis");
        }
    }
}

/// <summary>
/// Validator for SMTP configuration
/// </summary>
public class SmtpConfigurationValidator : IConfigurationValidator
{
    private readonly IConfiguration _configuration;

    public SmtpConfigurationValidator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string SectionName => "Smtp";
    public int Priority => 70;

    public ConfigurationValidationResult Validate()
    {
        var result = new ConfigurationValidationResult { SectionName = SectionName };
        var smtpSection = _configuration.GetSection(SectionName);

        if (!smtpSection.Exists())
        {
            result.AddWarning(SectionName, "SMTP configuration section is missing", "Add SMTP configuration for email functionality");
            return result;
        }

        // Validate Host
        var host = smtpSection["Host"];
        if (string.IsNullOrEmpty(host))
        {
            result.AddError("Smtp:Host", "SMTP Host is required", "Set the SMTP server hostname");
        }

        // Validate Port
        var portStr = smtpSection["Port"];
        if (!string.IsNullOrEmpty(portStr))
        {
            if (!int.TryParse(portStr, out var port))
            {
                result.AddError("Smtp:Port", "SMTP Port must be a valid integer", "Use a numeric port value");
            }
            else if (port <= 0 || port > 65535)
            {
                result.AddError("Smtp:Port", "SMTP Port must be between 1 and 65535", "Use a valid port number");
            }
            else if (port == 25)
            {
                result.AddWarning("Smtp:Port", "Using port 25 for SMTP", "Consider using port 587 (STARTTLS) or 465 (SSL) for better security");
            }
        }

        // Validate Username
        var username = smtpSection["Username"];
        if (string.IsNullOrEmpty(username))
        {
            result.AddWarning("Smtp:Username", "SMTP Username is not configured", "Set username for SMTP authentication");
        }

        // Validate Password
        var password = smtpSection["Password"];
        if (string.IsNullOrEmpty(password))
        {
            result.AddWarning("Smtp:Password", "SMTP Password is not configured", "Set password for SMTP authentication");
        }

        // Validate EnableSsl
        var enableSslStr = smtpSection["EnableSsl"];
        if (!string.IsNullOrEmpty(enableSslStr))
        {
            if (!bool.TryParse(enableSslStr, out var enableSsl))
            {
                result.AddError("Smtp:EnableSsl", "SMTP EnableSsl must be true or false", "Set EnableSsl to true or false");
            }
            else if (!enableSsl)
            {
                result.AddWarning("Smtp:EnableSsl", "SSL is disabled for SMTP", "Enable SSL for secure email transmission");
            }
        }

        return result;
    }
}

/// <summary>
/// Validator for rate limiting configuration
/// </summary>
public class RateLimitingConfigurationValidator : IConfigurationValidator
{
    private readonly IOptionsMonitor<DistributedRateLimitingOptions> _options;

    public RateLimitingConfigurationValidator(IOptionsMonitor<DistributedRateLimitingOptions> options)
    {
        _options = options;
    }

    public string SectionName => DistributedRateLimitingOptions.SectionName;
    public int Priority => 60;

    public ConfigurationValidationResult Validate()
    {
        var result = new ConfigurationValidationResult { SectionName = SectionName };
        
        try
        {
            var config = _options.CurrentValue;

            // Validate request limits
            if (config.RequestsPerMinute <= 0)
            {
                result.AddError("RequestsPerMinute", "RequestsPerMinute must be greater than 0", "Set a positive value for requests per minute");
            }
            else if (config.RequestsPerMinute > 10000)
            {
                result.AddWarning("RequestsPerMinute", "RequestsPerMinute is very high", "Consider if such a high rate limit is necessary");
            }

            if (config.RequestsPerHour <= 0)
            {
                result.AddError("RequestsPerHour", "RequestsPerHour must be greater than 0", "Set a positive value for requests per hour");
            }

            if (config.RequestsPerDay <= 0)
            {
                result.AddError("RequestsPerDay", "RequestsPerDay must be greater than 0", "Set a positive value for requests per day");
            }

            // Validate consistency between time windows
            if (config.RequestsPerMinute * 60 > config.RequestsPerHour)
            {
                result.AddWarning("RateLimit", "Minute rate limit allows more requests per hour than hour limit", "Ensure rate limits are consistent across time windows");
            }

            if (config.RequestsPerHour * 24 > config.RequestsPerDay)
            {
                result.AddWarning("RateLimit", "Hour rate limit allows more requests per day than day limit", "Ensure rate limits are consistent across time windows");
            }

            // Validate Redis configuration if enabled
            if (!string.IsNullOrEmpty(config.Redis.ConnectionString))
            {
                if (config.Redis.ConnectTimeout <= 0)
                {
                    result.AddError("Redis:ConnectTimeout", "Redis ConnectTimeout must be greater than 0", "Set a positive connection timeout");
                }

                if (config.Redis.CommandTimeout <= 0)
                {
                    result.AddError("Redis:CommandTimeout", "Redis CommandTimeout must be greater than 0", "Set a positive command timeout");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            result.AddError(SectionName, $"Failed to load rate limiting configuration: {ex.Message}", "Check the configuration format and values");
            return result;
        }
    }
}