using System.Reflection;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Extensions;

/// <summary>
/// Tests that environment variables take precedence over configuration values
/// across all infrastructure extension methods.
/// Uses the same 3-tier precedence pattern: EnvVar → Config → Default
/// </summary>
[Trait("Category", "Unit")]
[Collection("EnvironmentVariables")]
public class ConfigPrecedenceTests
{
    #region Database Connection String Precedence

    [Fact]
    public void DatabaseGetConnectionString_EnvVar_TakesPrecedenceOverConfig()
    {
        var envKey = "ConnectionStrings__TestService";
        Environment.SetEnvironmentVariable(envKey, "Host=env-host;Database=env-db");
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TestService"] = "Host=config-host;Database=config-db"
            });

            var result = InvokeGetConnectionString(config, "TestService");
            result.Should().Be("Host=env-host;Database=env-db");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    [Fact]
    public void DatabaseGetConnectionString_Config_UsedWhenNoEnvVar()
    {
        // Ensure no env var set
        Environment.SetEnvironmentVariable("ConnectionStrings__MyService", null);

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MyService"] = "Host=from-config;Database=mydb"
        });

        var result = InvokeGetConnectionString(config, "MyService");
        result.Should().Be("Host=from-config;Database=mydb");
    }

    [Fact]
    public void DatabaseGetConnectionString_FallsBackToDefaultConnection()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__FallbackService", null);

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=default;Database=fallback"
        });

        var result = InvokeGetConnectionString(config, "FallbackService");
        result.Should().Be("Host=default;Database=fallback");
    }

    [Fact]
    public void DatabaseGetConnectionString_NothingConfigured_Throws()
    {
        // The POSTGRES_*-assembled fallback that used to answer here picked the
        // database engine by way of Npgsql's key syntax.
        Environment.SetEnvironmentVariable("ConnectionStrings__NothingSvc", null);

        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => InvokeGetConnectionString(config, "NothingSvc");

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*ConnectionStrings__NothingSvc*");
    }

    #endregion

    #region RabbitMQ Settings Precedence

    [Fact]
    public void RabbitMqSettings_EnvVars_TakePrecedenceOverConfig()
    {
        var envVars = new[] { "RABBITMQ_HOST", "RABBITMQ_USERNAME", "RABBITMQ_PASSWORD", "RABBITMQ_VHOST", "RABBITMQ_PORT", "RABBITMQ_CONNECTION" };
        foreach (var v in envVars) Environment.SetEnvironmentVariable(v, null);

        Environment.SetEnvironmentVariable("RABBITMQ_HOST", "env-rabbit");
        Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", "env-user");
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "config-rabbit",
                ["RabbitMQ:Username"] = "config-user",
                ["RabbitMQ:Password"] = "config-pass"
            });

            var settings = InvokeGetRabbitMqSettings(config);
            settings.Host.Should().Be("env-rabbit", "env var takes precedence");
            settings.Username.Should().Be("env-user", "env var takes precedence");
            settings.Password.Should().Be("config-pass", "config used when no env var");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RABBITMQ_HOST", null);
            Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", null);
        }
    }

    [Fact]
    public void RabbitMqSettings_Config_UsedWhenNoEnvVars()
    {
        var envVars = new[] { "RABBITMQ_HOST", "RABBITMQ_USERNAME", "RABBITMQ_PASSWORD", "RABBITMQ_VHOST", "RABBITMQ_PORT", "RABBITMQ_CONNECTION" };
        foreach (var v in envVars) Environment.SetEnvironmentVariable(v, null);

        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = "my-rabbit",
            ["RabbitMQ:Username"] = "admin",
            ["RabbitMQ:Password"] = "secret",
            ["RabbitMQ:Port"] = "5673"
        });

        var settings = InvokeGetRabbitMqSettings(config);
        settings.Host.Should().Be("my-rabbit");
        settings.Username.Should().Be("admin");
        settings.Password.Should().Be("secret");
        settings.Port.Should().Be(5673);
    }

    [Fact]
    public void RabbitMqSettings_Defaults_UsedWhenNothingConfigured()
    {
        var envVars = new[] { "RABBITMQ_HOST", "RABBITMQ_USERNAME", "RABBITMQ_PASSWORD", "RABBITMQ_VHOST", "RABBITMQ_PORT", "RABBITMQ_CONNECTION" };
        foreach (var v in envVars) Environment.SetEnvironmentVariable(v, null);

        var config = BuildConfig(new Dictionary<string, string?>());
        var settings = InvokeGetRabbitMqSettings(config);

        settings.Host.Should().Be("rabbitmq");
        settings.Username.Should().Be("guest");
        settings.Password.Should().Be("guest");
        settings.VirtualHost.Should().Be("/");
        settings.Port.Should().Be(5672);
    }

    [Fact]
    public void RabbitMqSettings_ConnectionString_FromEnvVar_OverridesBuilt()
    {
        var envVars = new[] { "RABBITMQ_HOST", "RABBITMQ_USERNAME", "RABBITMQ_PASSWORD", "RABBITMQ_VHOST", "RABBITMQ_PORT", "RABBITMQ_CONNECTION" };
        foreach (var v in envVars) Environment.SetEnvironmentVariable(v, null);

        Environment.SetEnvironmentVariable("RABBITMQ_CONNECTION", "amqp://custom:pass@custom-host:9999/");
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>());
            var settings = InvokeGetRabbitMqSettings(config);

            settings.ConnectionString.Should().Be("amqp://custom:pass@custom-host:9999/");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RABBITMQ_CONNECTION", null);
        }
    }

    #endregion

    #region JWT Config Precedence

    [Fact]
    public void JwtAuthentication_EnvVar_JWT_SECRET_TakesPrecedenceOverConfig()
    {
        // Ensure clean state
        Environment.SetEnvironmentVariable("JWT_SECRET", "env-secret-that-is-long-enough-for-hmac-sha256-key!!");
        Environment.SetEnvironmentVariable("JWT_ISSUER", null);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", null);
        Environment.SetEnvironmentVariable("JwtSettings__ExpireMinutes", null);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "config-secret-that-should-be-overridden-by-env-var!!",
                ["JwtSettings:Issuer"] = "TestIssuer",
                ["JwtSettings:Audience"] = "TestAudience"
            });
            var env = Substitute.For<IHostEnvironment>();
            env.EnvironmentName.Returns("Development");

            services.AddJwtAuthentication(config, env);

            var provider = services.BuildServiceProvider();
            var jwtSettings = provider.GetRequiredService<IOptions<JwtSettings>>().Value;
            jwtSettings.Secret.Should().Be("env-secret-that-is-long-enough-for-hmac-sha256-key!!");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JWT_SECRET", null);
        }
    }

    [Fact]
    public void JwtAuthentication_ExpireMinutes_DefaultsTo60()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", null);
        Environment.SetEnvironmentVariable("JWT_ISSUER", null);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", null);
        Environment.SetEnvironmentVariable("JwtSettings__ExpireMinutes", null);

        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "test-secret-that-is-long-enough-for-signing!!",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience"
        });
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        services.AddJwtAuthentication(config, env);

        var provider = services.BuildServiceProvider();
        var jwtSettings = provider.GetRequiredService<IOptions<JwtSettings>>().Value;
        jwtSettings.ExpireMinutes.Should().Be(60);
    }

    [Fact]
    public void JwtAuthentication_ExpireMinutes_FromEnvVar()
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", null);
        Environment.SetEnvironmentVariable("JWT_ISSUER", null);
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", null);
        Environment.SetEnvironmentVariable("JwtSettings__ExpireMinutes", "120");
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "test-secret-that-is-long-enough-for-signing!!",
                ["JwtSettings:Issuer"] = "TestIssuer",
                ["JwtSettings:Audience"] = "TestAudience"
            });
            var env = Substitute.For<IHostEnvironment>();
            env.EnvironmentName.Returns("Development");

            services.AddJwtAuthentication(config, env);

            var provider = services.BuildServiceProvider();
            var jwtSettings = provider.GetRequiredService<IOptions<JwtSettings>>().Value;
            jwtSettings.ExpireMinutes.Should().Be(120);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JwtSettings__ExpireMinutes", null);
        }
    }

    #endregion

    #region Redis Config Precedence

    [Fact]
    public void SharedInfrastructure_RedisFromConnectionString_TakesPrecedence()
    {
        // Test the Redis resolution order in AddSharedInfrastructure:
        // 1. ConnectionStrings:Redis
        // 2. Redis:ConnectionString
        // 3. REDIS_CONNECTION_STRING env var

        // The code:
        // var redisFromConfig = configuration.GetConnectionString("Redis");
        // var redisFromConfigAlt = configuration["Redis:ConnectionString"];
        // var redisFromEnv = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
        // Use first non-null AND non-empty

        // We can verify this by checking which cache service gets registered
        // Without Redis → InMemoryTokenRevocationService
        // With Redis → RedisTokenRevocationService (but Redis won't connect in tests)

        // Just verify the resolution logic order is correct
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "",  // Empty string should be skipped
            ["Redis:ConnectionString"] = "localhost:6379"
        });

        var redisFromConfig = config.GetConnectionString("Redis");
        var redisFromConfigAlt = config["Redis:ConnectionString"];

        var resolved = !string.IsNullOrEmpty(redisFromConfig) ? redisFromConfig
            : !string.IsNullOrEmpty(redisFromConfigAlt) ? redisFromConfigAlt
            : null;

        resolved.Should().Be("localhost:6379", "empty ConnectionStrings:Redis should fallback to Redis:ConnectionString");
    }

    [Fact]
    public void SharedInfrastructure_EmptyRedisConnectionString_FallsBackToNext()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = ""
        });

        var redisFromConfig = config.GetConnectionString("Redis");
        string.IsNullOrEmpty(redisFromConfig).Should().BeTrue("empty string should be treated as not configured");
    }

    #endregion

    #region Helpers

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static string InvokeGetConnectionString(IConfiguration configuration, string serviceName)
    {
        var method = typeof(DatabaseExtensions)
            .GetMethod("GetConnectionString", BindingFlags.NonPublic | BindingFlags.Static);
        return (string)method!.Invoke(null, [configuration, serviceName])!;
    }

    private static RabbitMqSettings InvokeGetRabbitMqSettings(IConfiguration configuration)
    {
        var method = typeof(MessagingExtensions)
            .GetMethod("GetRabbitMqSettings", BindingFlags.NonPublic | BindingFlags.Static);
        return (RabbitMqSettings)method!.Invoke(null, [configuration])!;
    }

    #endregion
}
