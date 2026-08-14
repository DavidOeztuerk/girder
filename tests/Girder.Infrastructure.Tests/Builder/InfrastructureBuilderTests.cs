using Infrastructure.Builder;
using Infrastructure.Builder.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Tests.Builder;

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
    public void AllModuleFlags_DefaultToFalse()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.JwtEnabled.Should().BeFalse();
        builder.SecretManagementEnabled.Should().BeFalse();
        builder.EncryptionEnabled.Should().BeFalse();
        builder.RateLimitingEnabled.Should().BeFalse();
        builder.CachingEnabled.Should().BeFalse();
        builder.ResilienceEnabled.Should().BeFalse();
        builder.HealthChecksEnabled.Should().BeFalse();
        builder.SecurityHeadersEnabled.Should().BeFalse();
        builder.InputSanitizationEnabled.Should().BeFalse();
        builder.AuditEnabled.Should().BeFalse();
        builder.ComplianceEnabled.Should().BeFalse();
        builder.CommunicationEnabled.Should().BeFalse();
        builder.ObservabilityEnabled.Should().BeFalse();
        builder.AuthorizationEnabled.Should().BeFalse();
        builder.SecurityMonitoringEnabled.Should().BeFalse();
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
    public void AddCompliance_SetsFlag()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        builder.AddCompliance();

        builder.ComplianceEnabled.Should().BeTrue();
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

    [Fact]
    public void FluentChaining_Works()
    {
        var builder = new InfrastructureBuilder(_services, _configuration, _environment, "TestService");

        var result = builder
            .AddResilience()
            .AddInputSanitization()
            .AddSecurityHeaders()
            .AddAuditLogging()
            .AddHealthChecks()
            .AddCompliance();

        result.Should().BeSameAs(builder);
        builder.ResilienceEnabled.Should().BeTrue();
        builder.InputSanitizationEnabled.Should().BeTrue();
        builder.SecurityHeadersEnabled.Should().BeTrue();
        builder.AuditEnabled.Should().BeTrue();
        builder.HealthChecksEnabled.Should().BeTrue();
        builder.ComplianceEnabled.Should().BeTrue();
    }
}
