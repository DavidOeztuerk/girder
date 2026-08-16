using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class InfrastructureBuilderTests
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();

    public InfrastructureBuilderTests()
    {
        var configData = new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "TestSecret-AtLeast32Characters-Long!",
            ["JwtSettings:Issuer"] = "test-issuer",
            ["JwtSettings:Audience"] = "test-audience"
        };
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
        _environment.EnvironmentName.Returns("Development");
    }

    [Fact]
    public void Constructor_ResolvesRedisFromConfig()
    {
        var configData = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:6379"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var builder = new InfrastructureBuilder(_services, config, _environment, "TestService");

        builder.RedisConnectionString.Should().Be("localhost:6379");
    }

    [Fact]
    public void Constructor_RedisNull_WhenNotConfigured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var builder = new InfrastructureBuilder(_services, config, _environment, "TestService");

        builder.RedisConnectionString.Should().BeNull();
    }

    [Fact]
    public void Constructor_SetsProperties()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.Services.Should().BeSameAs(_services);
        builder.Configuration.Should().BeSameAs(_configuration);
        builder.Environment.Should().BeSameAs(_environment);
        builder.ServiceName.Should().Be("TestService");
    }

    [Fact]
    public void AddResilience_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddResilience();

        builder.ResilienceEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddInputSanitization_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddInputSanitization();

        builder.InputSanitizationEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddSecurityHeaders_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddSecurityHeaders();

        builder.SecurityHeadersEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddAuditLogging_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddAuditLogging();

        builder.AuditEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddHealthChecks_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddHealthChecks();

        builder.HealthChecksEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddEncryption_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddEncryption();

        builder.EncryptionEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddDistributedRateLimiting_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddDistributedRateLimiting();

        builder.RateLimitingEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddAuthorization_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddAuthorization();

        builder.AuthorizationEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddObservability_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddObservability();

        builder.ObservabilityEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddSecurityMonitoring_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddSecurityMonitoring();

        builder.SecurityMonitoringEnabled.Should().BeTrue();
    }
}
