using Infrastructure.Configuration;
using Infrastructure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests.Configuration;

[Trait("Category", "Unit")]
public class ConfigurationValidatorTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    #region ConfigurationValidationResult

    [Fact]
    public void ValidationResult_NewInstance_IsValidTrue()
    {
        var result = new ConfigurationValidationResult();

        result.IsValid.Should().BeTrue("a fresh result with no errors should be valid");
    }

    [Fact]
    public void ValidationResult_AddError_SetsIsValidFalse()
    {
        var result = new ConfigurationValidationResult { IsValid = true };

        result.AddError("key", "message");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Key.Should().Be("key");
        result.Errors[0].Message.Should().Be("message");
    }

    [Fact]
    public void ValidationResult_AddError_WithSuggestion()
    {
        var result = new ConfigurationValidationResult();

        result.AddError("key", "msg", "fix it");

        result.Errors[0].Suggestion.Should().Be("fix it");
    }

    [Fact]
    public void ValidationResult_AddWarning_DoesNotAffectIsValid()
    {
        var result = new ConfigurationValidationResult { IsValid = true };

        result.AddWarning("key", "warning");

        result.IsValid.Should().BeTrue("warnings do not affect IsValid");
        result.Warnings.Should().HaveCount(1);
    }

    [Fact]
    public void ValidationResult_Combine_AllValid_IsValid()
    {
        var r1 = new ConfigurationValidationResult { IsValid = true };
        var r2 = new ConfigurationValidationResult { IsValid = true };

        var combined = ConfigurationValidationResult.Combine(r1, r2);

        combined.IsValid.Should().BeTrue();
        combined.SectionName.Should().Be("Combined");
    }

    [Fact]
    public void ValidationResult_Combine_OneInvalid_IsInvalid()
    {
        var r1 = new ConfigurationValidationResult { IsValid = true };
        var r2 = new ConfigurationValidationResult { IsValid = true };
        r2.AddError("key", "error");

        var combined = ConfigurationValidationResult.Combine(r1, r2);

        combined.IsValid.Should().BeFalse();
        combined.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void ValidationResult_Combine_MergesAllErrorsAndWarnings()
    {
        var r1 = new ConfigurationValidationResult { IsValid = true };
        r1.AddWarning("w1", "warn1");
        var r2 = new ConfigurationValidationResult { IsValid = true };
        r2.AddWarning("w2", "warn2");
        r2.AddError("e1", "err1");

        var combined = ConfigurationValidationResult.Combine(r1, r2);

        combined.Errors.Should().HaveCount(1);
        combined.Warnings.Should().HaveCount(2);
    }

    #endregion

    #region JwtConfigurationValidator

    [Fact]
    public void JwtValidator_MissingSection_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("missing"));
    }

    [Fact]
    public void JwtValidator_SectionName_IsJwt()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var validator = new JwtConfigurationValidator(config);

        validator.SectionName.Should().Be("Jwt");
        validator.Priority.Should().Be(100);
    }

    [Fact]
    public void JwtValidator_EmptySecret_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Jwt:Secret" && e.Message.Contains("required"));
    }

    [Fact]
    public void JwtValidator_ShortSecret_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "short",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("too short"));
    }

    [Fact]
    public void JwtValidator_PlaceholderSecret_TooShort_ReturnsShortError()
    {
        // "your-secret-key" is only 15 chars, so the < 32 check fires first
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "your-secret-key",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("too short"));
    }

    [Theory]
    [InlineData("this-is-an-example-secret-for-testing-only!!")]
    [InlineData("sample-key-that-is-long-enough-for-minimum!!")]
    public void JwtValidator_SecretContainingExampleOrSample_ReturnsError(string secret)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = secret,
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("placeholder"));
    }

    [Fact]
    public void JwtValidator_MissingIssuer_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Audience"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Jwt:Issuer");
    }

    [Fact]
    public void JwtValidator_MissingAudience_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Jwt:Audience");
    }

    [Fact]
    public void JwtValidator_InvalidExpirationInMinutes_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test",
            ["Jwt:ExpirationInMinutes"] = "not-a-number"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Jwt:ExpirationInMinutes");
    }

    [Fact]
    public void JwtValidator_NegativeExpiration_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test",
            ["Jwt:ExpirationInMinutes"] = "-5"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("greater than 0"));
    }

    [Fact]
    public void JwtValidator_ZeroExpiration_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test",
            ["Jwt:ExpirationInMinutes"] = "0"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void JwtValidator_VeryLongExpiration_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test",
            ["Jwt:ExpirationInMinutes"] = "50000"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().NotContain(e => e.Key == "Jwt:ExpirationInMinutes");
        result.Warnings.Should().Contain(w => w.Key == "Jwt:ExpirationInMinutes");
    }

    [Fact]
    public void JwtValidator_ValidConfig_NoErrors()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "SkillswapTest",
            ["Jwt:Audience"] = "SkillswapTestAudience",
            ["Jwt:ExpirationInMinutes"] = "60"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void JwtValidator_NoExpirationInMinutes_NoErrors()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = "a-valid-secret-key-that-is-at-least-32-characters-long!!",
            ["Jwt:Issuer"] = "test",
            ["Jwt:Audience"] = "test"
        });
        var validator = new JwtConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty("ExpirationInMinutes is optional");
    }

    #endregion

    #region DatabaseConfigurationValidator

    [Fact]
    public void DbValidator_MissingSection_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("missing"));
    }

    [Fact]
    public void DbValidator_SectionName_IsConnectionStrings()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var validator = new DatabaseConfigurationValidator(config);

        validator.SectionName.Should().Be("ConnectionStrings");
        validator.Priority.Should().Be(90);
    }

    [Fact]
    public void DbValidator_MissingDefaultConnection_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SomeOther"] = "Host=localhost"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "ConnectionStrings:DefaultConnection");
    }

    [Fact]
    public void DbValidator_WeakPassword_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=test;password=123"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty("weak password is a warning, not an error");
        result.Warnings.Should().Contain(w => w.Message.Contains("weak password"));
    }

    [Fact]
    public void DbValidator_WeakPasswordAdmin_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=test;password=admin"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Message.Contains("weak password"));
    }

    [Fact]
    public void DbValidator_LocalhostInProduction_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Password=strong",
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Message.Contains("localhost"));
    }

    [Fact]
    public void DbValidator_LocalhostInDevelopment_NoWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Password=strong",
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().NotContain(w => w.Message.Contains("localhost"));
    }

    [Fact]
    public void DbValidator_IntegratedSecurity_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=db;Database=test;Integrated Security=true"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Message.Contains("integrated security"));
    }

    [Fact]
    public void DbValidator_RedisLocalhostInProduction_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.prod;Database=test;Password=strong",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["ASPNETCORE_ENVIRONMENT"] = "Production"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "ConnectionStrings:Redis" && w.Message.Contains("localhost"));
    }

    [Fact]
    public void DbValidator_RedisNoPassword_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=test;Password=strong",
            ["ConnectionStrings:Redis"] = "redis-server:6379"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "ConnectionStrings:Redis" && w.Message.Contains("password"));
    }

    [Fact]
    public void DbValidator_ValidConfig_NoErrors()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db.prod;Database=skillswap;Password=SecureP@ss"
        });
        var validator = new DatabaseConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    #endregion

    #region SmtpConfigurationValidator

    [Fact]
    public void SmtpValidator_MissingSection_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        // SMTP section is optional — missing = warning, not error
        result.Errors.Should().BeEmpty("SMTP section is optional — missing is warning only");
        result.Warnings.Should().Contain(w => w.Message.Contains("missing"));
    }

    [Fact]
    public void SmtpValidator_SectionName_IsSmtp()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var validator = new SmtpConfigurationValidator(config);

        validator.SectionName.Should().Be("Smtp");
        validator.Priority.Should().Be(70);
    }

    [Fact]
    public void SmtpValidator_MissingHost_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Port"] = "587"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Smtp:Host");
    }

    [Fact]
    public void SmtpValidator_InvalidPort_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "not-a-number"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Smtp:Port" && e.Message.Contains("valid integer"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("65536")]
    [InlineData("99999")]
    public void SmtpValidator_PortOutOfRange_ReturnsError(string port)
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = port
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Smtp:Port" && e.Message.Contains("between 1 and 65535"));
    }

    [Fact]
    public void SmtpValidator_Port25_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "25"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().NotContain(e => e.Key == "Smtp:Port", "port 25 is valid but insecure");
        result.Warnings.Should().Contain(w => w.Key == "Smtp:Port" && w.Message.Contains("port 25"));
    }

    [Fact]
    public void SmtpValidator_MissingUsername_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "Smtp:Username");
    }

    [Fact]
    public void SmtpValidator_MissingPassword_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "Smtp:Password");
    }

    [Fact]
    public void SmtpValidator_InvalidEnableSsl_ReturnsError()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:EnableSsl"] = "maybe"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Smtp:EnableSsl");
    }

    [Fact]
    public void SmtpValidator_SslDisabled_ReturnsWarning()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:EnableSsl"] = "false"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "Smtp:EnableSsl" && w.Message.Contains("disabled"));
    }

    [Fact]
    public void SmtpValidator_ValidConfig_NoErrors()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:Port"] = "587",
            ["Smtp:Username"] = "user",
            ["Smtp:Password"] = "pass",
            ["Smtp:EnableSsl"] = "true"
        });
        var validator = new SmtpConfigurationValidator(config);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    #endregion

    #region RateLimitingConfigurationValidator

    private static RateLimitingConfigurationValidator CreateRateLimitValidator(DistributedRateLimitingOptions options)
    {
        var monitor = Substitute.For<IOptionsMonitor<DistributedRateLimitingOptions>>();
        monitor.CurrentValue.Returns(options);
        return new RateLimitingConfigurationValidator(monitor);
    }

    [Fact]
    public void RateLimitValidator_SectionName_IsDistributedRateLimiting()
    {
        var validator = CreateRateLimitValidator(new DistributedRateLimitingOptions());

        validator.SectionName.Should().Be("DistributedRateLimiting");
        validator.Priority.Should().Be(60);
    }

    [Fact]
    public void RateLimitValidator_DefaultOptions_NoErrors()
    {
        // Defaults: 100/min, 1000/hr, 10000/day
        // 100*60=6000 > 1000 and 1000*24=24000 > 10000 → cross-window warnings expected
        var validator = CreateRateLimitValidator(new DistributedRateLimitingOptions());

        var result = validator.Validate();

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().HaveCountGreaterThanOrEqualTo(2, "default limits trigger cross-window consistency warnings");
    }

    [Fact]
    public void RateLimitValidator_RequestsPerMinuteZero_ReturnsError()
    {
        var options = new DistributedRateLimitingOptions { RequestsPerMinute = 0 };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "RequestsPerMinute");
    }

    [Fact]
    public void RateLimitValidator_RequestsPerMinuteNegative_ReturnsError()
    {
        var options = new DistributedRateLimitingOptions { RequestsPerMinute = -10 };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void RateLimitValidator_RequestsPerMinuteVeryHigh_ReturnsWarning()
    {
        var options = new DistributedRateLimitingOptions { RequestsPerMinute = 20000 };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "RequestsPerMinute" && w.Message.Contains("very high"));
    }

    [Fact]
    public void RateLimitValidator_RequestsPerHourZero_ReturnsError()
    {
        var options = new DistributedRateLimitingOptions { RequestsPerHour = 0 };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "RequestsPerHour");
    }

    [Fact]
    public void RateLimitValidator_RequestsPerDayZero_ReturnsError()
    {
        var options = new DistributedRateLimitingOptions { RequestsPerDay = 0 };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "RequestsPerDay");
    }

    [Fact]
    public void RateLimitValidator_MinuteExceedsHour_ReturnsWarning()
    {
        // 200 * 60 = 12000 > 1000
        var options = new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 200,
            RequestsPerHour = 1000
        };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "RateLimit" && w.Message.Contains("Minute"));
    }

    [Fact]
    public void RateLimitValidator_HourExceedsDay_ReturnsWarning()
    {
        // 1000 * 24 = 24000 > 10000
        var options = new DistributedRateLimitingOptions
        {
            RequestsPerHour = 1000,
            RequestsPerDay = 10000
        };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.Warnings.Should().Contain(w => w.Key == "RateLimit" && w.Message.Contains("Hour"));
    }

    [Fact]
    public void RateLimitValidator_ConsistentLimits_NoWarnings()
    {
        var options = new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 10,
            RequestsPerHour = 600,
            RequestsPerDay = 14400
        };
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void RateLimitValidator_RedisConnectTimeoutZero_ReturnsError()
    {
        var options = new DistributedRateLimitingOptions();
        options.Redis.ConnectionString = "redis:6379";
        options.Redis.ConnectTimeout = 0;
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Redis:ConnectTimeout");
    }

    [Fact]
    public void RateLimitValidator_RedisCommandTimeoutZero_ReturnsError()
    {
        var options = new DistributedRateLimitingOptions();
        options.Redis.ConnectionString = "redis:6379";
        options.Redis.CommandTimeout = 0;
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Key == "Redis:CommandTimeout");
    }

    [Fact]
    public void RateLimitValidator_EmptyRedisConnectionString_SkipsRedisValidation()
    {
        var options = new DistributedRateLimitingOptions
        {
            // Use consistent limits to avoid cross-window warnings
            RequestsPerMinute = 10,
            RequestsPerHour = 600,
            RequestsPerDay = 14400
        };
        options.Redis.ConnectionString = "";
        options.Redis.ConnectTimeout = 0; // would be invalid if Redis validation ran
        var validator = CreateRateLimitValidator(options);

        var result = validator.Validate();

        result.Errors.Should().BeEmpty("Redis validation is skipped when connection string is empty");
    }

    [Fact]
    public void RateLimitValidator_ExceptionInOptionsMonitor_ReturnsError()
    {
        var monitor = Substitute.For<IOptionsMonitor<DistributedRateLimitingOptions>>();
        monitor.CurrentValue.Returns(_ => throw new System.InvalidOperationException("Config broken"));
        var validator = new RateLimitingConfigurationValidator(monitor);

        var result = validator.Validate();

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Failed to load"));
    }

    #endregion
}
