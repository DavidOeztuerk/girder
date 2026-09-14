using Noelia.Abstractions.Security.Encryption;
using Noelia.Abstractions.Security.Audit;
using Noelia.Abstractions.Security.Secrets;
using Noelia.Redis.Security;
using Noelia.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Noelia.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityExtensionsTests
{
    [Fact]
    public void AddSecretManagement_LeavesTheImplementationToAProviderPackage()
    {
        // Choosing an ISecretProvider means choosing where secrets live, which
        // is the operator's decision.
        var services = new ServiceCollection();

        services.AddSecretManagement();

        services.Should().NotContain(d => d.ServiceType == typeof(ISecretProvider));
    }

    [Fact]
    public void AddRedisSecretProvider_RegistersTheRedisImplementation()
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

        services.AddRedisSecretProvider(configuration, environment);

        using var provider = services.BuildServiceProvider();
        var unversioned = provider.GetRequiredService<ISecretProvider>();
        unversioned.Should().BeOfType<SecretManager>();
        provider.GetRequiredService<IVersionedSecretProvider>().Should().BeSameAs(unversioned);
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
    public void AddRedisSecurityAudit_UsesTheRegisteredMasterKeyProvider()
    {
        var services = new ServiceCollection();
        var connection = Substitute.For<IConnectionMultiplexer>();
        connection.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Returns(Substitute.For<IDatabase>());
        var masterKey = Substitute.For<IMasterKeyProvider>();
        masterKey.GetMasterKey().Returns(Enumerable.Repeat((byte)0x5A, 32).ToArray());
        services.AddSingleton(connection);
        services.AddSingleton(masterKey);
        services.AddLogging();

        services.AddRedisSecurityAudit();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ISecurityAuditService>()
            .Should().BeOfType<Noelia.Redis.Security.Audit.SecurityAuditService>();
    }

    [Fact]
    public void AddRedisSecurityAudit_WithoutAKeyProviderFailsClearly()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddLogging();
        services.AddRedisSecurityAudit();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ISecurityAuditService>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*IMasterKeyProvider*");
    }

    [Fact]
    public void Duplicate_and_unconsumed_secret_contracts_are_not_shipped()
    {
        typeof(ISecretProvider).Assembly
            .GetType("Noelia.Abstractions.Security.ISecretManager")
            .Should().BeNull();
        typeof(SecurityExtensions).Assembly
            .GetType("Noelia.Infrastructure.Security.SecretRotationOptions")
            .Should().BeNull();
        typeof(SecurityExtensions).Assembly
            .GetType("Noelia.Infrastructure.Security.Secrets.SecureSecretManager")
            .Should().BeNull();
    }
}
