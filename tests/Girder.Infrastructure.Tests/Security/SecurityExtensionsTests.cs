using Girder.Redis.Security;
using Girder.Abstractions.Security;
using Girder.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityExtensionsTests
{
    [Fact]
    public void AddSecretManagement_LeavesTheImplementationToAProviderPackage()
    {
        // Choosing an ISecretManager means choosing where secrets live, which is
        // the operator's decision. AddRedisSecretManager() or
        // AddInMemorySecretManager() makes it explicit.
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        // Register required dependencies
        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        services.AddSingleton(connectionMultiplexer);
        services.AddLogging();

        services.AddSecretManagement(configuration, environment);

        services.Should().NotContain(d => d.ServiceType == typeof(ISecretManager));
    }

    [Fact]
    public void AddRedisSecretManager_RegistersTheRedisImplementation()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Returns(Substitute.For<IDatabase>());
        services.AddSingleton(connectionMultiplexer);
        services.AddLogging();

        services.AddRedisSecretManager(configuration, environment);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecretManager>().Should().BeOfType<SecretManager>();
    }

    [Fact]
    public void AddSecurityAuditLogging_RegistersAuditLogger()
    {
        var services = new ServiceCollection();

        services.AddSecurityAuditLogging();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecurityAuditLogger));
        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(SecurityAuditLogger));
    }

    [Fact]
    public void SecretRotationOptions_HasDefaultValues()
    {
        var options = new SecretRotationOptions();

        options.EnableRotation.Should().BeTrue();
        options.RotationIntervalHours.Should().Be(24 * 7);
        options.KeepOldSecretsCount.Should().Be(3);
        options.SecretsToRotate.Should().Contain("JwtSecret");
        options.SecretsToRotate.Should().Contain("EncryptionKey");
    }
}
