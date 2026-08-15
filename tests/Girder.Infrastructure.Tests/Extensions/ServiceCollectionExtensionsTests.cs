using System.Reflection;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Girder.Core.Exceptions;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class ServiceCollectionExtensionsTests
{
    #region Helper Methods

    private static IConfiguration BuildConfiguration(Dictionary<string, string?>? values = null)
    {
        var builder = new ConfigurationBuilder();
        if (values != null)
        {
            builder.AddInMemoryCollection(values);
        }
        return builder.Build();
    }

    private static IHostEnvironment CreateEnvironment(string environmentName = "Development")
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    private static MethodInfo GetPrivateStaticMethod(string methodName)
    {
        var method = typeof(Girder.Infrastructure.Extensions.ServiceCollectionExtensions)
            .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);

        if (method == null)
            throw new System.InvalidOperationException($"Method '{methodName}' not found on ServiceCollectionExtensions");

        return method;
    }

    /// <summary>
    /// Safely sets environment variables for a test and restores them afterwards.
    /// Returns an IDisposable that restores the original values.
    /// </summary>
    private static EnvVarScope SetEnvironmentVariables(params (string key, string? value)[] variables)
    {
        return new EnvVarScope(variables);
    }

    private sealed class EnvVarScope : IDisposable
    {
        private readonly (string key, string? originalValue)[] _originals;

        public EnvVarScope((string key, string? value)[] variables)
        {
            _originals = variables
                .Select(v => (v.key, Environment.GetEnvironmentVariable(v.key)))
                .ToArray();

            foreach (var (key, value) in variables)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        public void Dispose()
        {
            foreach (var (key, originalValue) in _originals)
            {
                Environment.SetEnvironmentVariable(key, originalValue);
            }
        }
    }

    #endregion

    #region AddCaching Tests

    [Fact]
    public void AddCaching_EmptyConnectionString_RegistersMemoryDistributedCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCaching(string.Empty);

        // Assert
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDistributedCache) &&
            d.ImplementationType == typeof(MemoryDistributedCache));
    }

    [Fact]
    public void AddCaching_NullishWhitespaceConnectionString_RegistersMemoryDistributedCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCaching("   ");

        // Assert
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDistributedCache) &&
            d.ImplementationType == typeof(MemoryDistributedCache));
    }

    [Fact]
    public void AddCaching_InvalidRedisConnectionString_FallsBackToMemoryDistributedCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - use an invalid connection string that will fail to connect
        services.AddCaching("invalid-host:99999,connectTimeout=1,syncTimeout=1,abortConnect=true");

        // Assert - should fall back to MemoryDistributedCache after connection failure
        services.Should().Contain(d =>
            d.ServiceType == typeof(IDistributedCache) &&
            d.ImplementationType == typeof(MemoryDistributedCache));
    }

    [Fact]
    public void AddCaching_AlwaysRegistersMemoryCache()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCaching(string.Empty);

        // Assert - MemoryCache is always registered (for rate limiting)
        services.Should().Contain(d =>
            d.ServiceType == typeof(Microsoft.Extensions.Caching.Memory.IMemoryCache));
    }

    #endregion

    #region AddJwtAuthentication Tests

    [Fact]
    public void AddJwtAuthentication_MissingSecret_ThrowsConfigurationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration();
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        var act = () => services.AddJwtAuthentication(config, env);

        // Assert
        act.Should().Throw<ConfigurationException>()
            .Which.ConfigurationKey.Should().Be("JWT_SECRET");
    }

    [Fact]
    public void AddJwtAuthentication_PlaceholderSecret_ThrowsConfigurationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "REPLACE_WITH_SECURE_SECRET_IN_PRODUCTION",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        var act = () => services.AddJwtAuthentication(config, env);

        // Assert
        act.Should().Throw<ConfigurationException>()
            .Which.ConfigurationKey.Should().Be("JWT_SECRET");
    }

    [Fact]
    public void AddJwtAuthentication_SecretContainingPlaceholderSubstring_ThrowsConfigurationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "prefix_REPLACE_WITH_SECURE_SECRET_IN_PRODUCTION_suffix",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        var act = () => services.AddJwtAuthentication(config, env);

        // Assert
        act.Should().Throw<ConfigurationException>();
    }

    [Fact]
    public void AddJwtAuthentication_MissingIssuer_ThrowsConfigurationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "super-secret-key-that-is-long-enough-for-jwt-signing",
            // No issuer
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        var act = () => services.AddJwtAuthentication(config, env);

        // Assert
        act.Should().Throw<ConfigurationException>()
            .Which.ConfigurationKey.Should().Be("JWT_ISSUER");
    }

    [Fact]
    public void AddJwtAuthentication_MissingAudience_ThrowsConfigurationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "super-secret-key-that-is-long-enough-for-jwt-signing",
            ["JwtSettings:Issuer"] = "TestIssuer"
            // No audience
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        var act = () => services.AddJwtAuthentication(config, env);

        // Assert
        act.Should().Throw<ConfigurationException>()
            .Which.ConfigurationKey.Should().Be("JWT_AUDIENCE");
    }

    [Fact]
    public void AddJwtAuthentication_ValidConfig_RegistersAuthenticationServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "super-secret-key-that-is-long-enough-for-jwt-signing",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        services.AddJwtAuthentication(config, env);

        // Assert - authentication services should be registered
        services.Should().Contain(d =>
            d.ServiceType == typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService));
    }

    [Fact]
    public void AddJwtAuthentication_ValidConfig_ConfiguresJwtSettingsOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "super-secret-key-that-is-long-enough-for-jwt-signing",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience",
            ["JwtSettings:ExpireMinutes"] = "120"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null),
            ("JwtSettings__ExpireMinutes", null));

        // Act
        services.AddJwtAuthentication(config, env);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JwtSettings>>().Value;

        options.Secret.Should().Be("super-secret-key-that-is-long-enough-for-jwt-signing");
        options.Issuer.Should().Be("TestIssuer");
        options.Audience.Should().Be("TestAudience");
        options.ExpireMinutes.Should().Be(120);
    }

    [Fact]
    public void AddJwtAuthentication_EnvVarSecretTakesPrecedence()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "config-secret-that-should-be-overridden-by-env",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", "env-secret-key-that-is-long-enough-for-jwt-signing"),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null));

        // Act
        services.AddJwtAuthentication(config, env);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JwtSettings>>().Value;

        options.Secret.Should().Be("env-secret-key-that-is-long-enough-for-jwt-signing");
    }

    [Fact]
    public void AddJwtAuthentication_ExpireMinutesNotConfigured_DefaultsTo60()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "super-secret-key-that-is-long-enough-for-jwt-signing",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience"
            // No ExpireMinutes
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("JWT_SECRET", null),
            ("JWT_ISSUER", null),
            ("JWT_AUDIENCE", null),
            ("JwtSettings__ExpireMinutes", null));

        // Act
        services.AddJwtAuthentication(config, env);

        // Assert
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<JwtSettings>>().Value;

        options.ExpireMinutes.Should().Be(60);
    }

    #endregion

    #region ResolveAllowedOrigins Tests (private, via reflection)

    [Fact]
    public void ResolveAllowedOrigins_NoConfig_Development_DefaultsToLocalhost3000()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ResolveAllowedOrigins");
        var config = BuildConfiguration();
        var env = CreateEnvironment("Development");

        using var scope = SetEnvironmentVariables(
            ("CORS_ORIGINS", null),
            ("FRONTEND_URL", null));

        // Act
        var result = (string[])method.Invoke(null, [config, env])!;

        // Assert
        result.Should().ContainSingle()
            .Which.Should().Be("http://localhost:3000");
    }

    [Fact]
    public void ResolveAllowedOrigins_NoConfig_Production_ReturnsEmpty()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ResolveAllowedOrigins");
        var config = BuildConfiguration();
        var env = CreateEnvironment("Production");

        using var scope = SetEnvironmentVariables(
            ("CORS_ORIGINS", null),
            ("FRONTEND_URL", null));

        // Act
        var result = (string[])method.Invoke(null, [config, env])!;

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveAllowedOrigins_CorsOriginsEnvVar_ParsedCorrectly()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ResolveAllowedOrigins");
        var config = BuildConfiguration();
        var env = CreateEnvironment("Production");

        using var scope = SetEnvironmentVariables(
            ("CORS_ORIGINS", "https://app.example.com, https://admin.example.com"),
            ("FRONTEND_URL", null));

        // Act
        var result = (string[])method.Invoke(null, [config, env])!;

        // Assert
        result.Should().Contain("https://app.example.com");
        result.Should().Contain("https://admin.example.com");
    }

    [Fact]
    public void ResolveAllowedOrigins_FrontendUrlAddedToOrigins()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ResolveAllowedOrigins");
        var config = BuildConfiguration();
        var env = CreateEnvironment("Production");

        using var scope = SetEnvironmentVariables(
            ("CORS_ORIGINS", null),
            ("FRONTEND_URL", "https://frontend.example.com"));

        // Act
        var result = (string[])method.Invoke(null, [config, env])!;

        // Assert
        result.Should().Contain("https://frontend.example.com");
    }

    [Fact]
    public void ResolveAllowedOrigins_DuplicateOrigins_Deduplicated()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ResolveAllowedOrigins");
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://app.example.com"
        });
        var env = CreateEnvironment("Production");

        using var scope = SetEnvironmentVariables(
            ("CORS_ORIGINS", "https://app.example.com"),
            ("FRONTEND_URL", null));

        // Act
        var result = (string[])method.Invoke(null, [config, env])!;

        // Assert
        result.Count(o => o.Equals("https://app.example.com", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public void ResolveAllowedOrigins_InvalidUrls_FilteredOut()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ResolveAllowedOrigins");
        var config = BuildConfiguration();
        var env = CreateEnvironment("Production");

        using var scope = SetEnvironmentVariables(
            ("CORS_ORIGINS", "https://valid.example.com,not-a-url,ftp://also-valid.com"),
            ("FRONTEND_URL", null));

        // Act
        var result = (string[])method.Invoke(null, [config, env])!;

        // Assert
        result.Should().Contain("https://valid.example.com");
        result.Should().NotContain("not-a-url");
    }

    #endregion

    #region NormalizeOrigin Tests (private static, via reflection)

    [Theory]
    [InlineData("https://example.com/path/to/resource", "https://example.com")]
    [InlineData("http://localhost:3000/api", "http://localhost:3000")]
    [InlineData("https://app.example.com:8443/", "https://app.example.com:8443")]
    public void NormalizeOrigin_ValidAbsoluteUrl_ReturnsAuthorityPart(string input, string expected)
    {
        // Arrange
        var method = GetPrivateStaticMethod("NormalizeOrigin");

        // Act
        var result = (string?)method.Invoke(null, [input]);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("just-text")]
    public void NormalizeOrigin_InvalidUrl_ReturnsNull(string input)
    {
        // Arrange
        var method = GetPrivateStaticMethod("NormalizeOrigin");

        // Act
        var result = (string?)method.Invoke(null, [input]);

        // Assert
        result.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeOrigin_EmptyOrNull_ReturnsNull(string? input)
    {
        // Arrange
        var method = GetPrivateStaticMethod("NormalizeOrigin");

        // Act
        var result = (string?)method.Invoke(null, [input]);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region ParseOrigins Tests (private static, via reflection)

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseOrigins_NullOrEmpty_ReturnsEmpty(string? input)
    {
        // Arrange
        var method = GetPrivateStaticMethod("ParseOrigins");

        // Act
        var result = ((IEnumerable<string>)method.Invoke(null, [input])!).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseOrigins_CommaSeparated_SplitsAndTrims()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ParseOrigins");

        // Act
        var result = ((IEnumerable<string>)method.Invoke(null, ["https://a.com , https://b.com , https://c.com"])!).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("https://a.com");
        result.Should().Contain("https://b.com");
        result.Should().Contain("https://c.com");
    }

    [Fact]
    public void ParseOrigins_SingleValue_ReturnsSingleElement()
    {
        // Arrange
        var method = GetPrivateStaticMethod("ParseOrigins");

        // Act
        var result = ((IEnumerable<string>)method.Invoke(null, ["https://single.com"])!).ToList();

        // Assert
        result.Should().ContainSingle()
            .Which.Should().Be("https://single.com");
    }

    #endregion

    #region AddSharedInfrastructure Tests

    [Fact]
    public void AddSharedInfrastructure_WithoutRedis_RegistersInMemoryTokenRevocationService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration();
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("REDIS_CONNECTION_STRING", null));

        // Act
        services.AddSharedInfrastructure(config, env, "TestService");

        // Assert
        services.Should().Contain(d =>
            d.ServiceType == typeof(ITokenRevocationService) &&
            d.ImplementationType == typeof(InMemoryTokenRevocationService));
    }

    [Theory]
    [InlineData("UserService")]
    [InlineData("Gateway")]
    public void AddSharedInfrastructure_RegistersServiceCommunication_WhateverTheServiceIsCalled(
        string serviceName)
    {
        // The library used to skip this for the literal name "Gateway". Which
        // services call peers is not something a name can answer — a service
        // that calls none simply does not add the module.
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ServiceCommunication:GatewayBaseUrl"] = "http://localhost:8080"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("REDIS_CONNECTION_STRING", null));

        services.AddSharedInfrastructure(config, env, serviceName);

        services.Should().Contain(d =>
            d.ServiceType == typeof(Girder.Infrastructure.Communication.IServiceCommunicationManager));
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersCoreSecurityServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration();
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("REDIS_CONNECTION_STRING", null));

        // Act
        services.AddSharedInfrastructure(config, env, "TestService");

        // Assert - core services should always be registered
        services.Should().Contain(d => d.ServiceType == typeof(IJwtService));
        services.Should().Contain(d => d.ServiceType == typeof(ITotpService));
        services.Should().Contain(d => d.ServiceType == typeof(ITokenRevocationService));
    }

    [Theory]
    [InlineData("UserService")]
    [InlineData("Gateway")]
    public void AddSharedInfrastructure_RegistersCacheInvalidationService_WhateverTheServiceIsCalled(
        string serviceName)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ServiceCommunication:GatewayBaseUrl"] = "http://localhost:8080"
        });
        var env = CreateEnvironment();

        using var scope = SetEnvironmentVariables(
            ("REDIS_CONNECTION_STRING", null));

        services.AddSharedInfrastructure(config, env, serviceName);

        services.Should().Contain(d =>
            d.ServiceType == typeof(Girder.Infrastructure.Caching.CacheInvalidationService));
    }

    #endregion
}
