using Girder.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityExtensionsTopUpTests
{
    [Fact]
    public void AddSecretManagement_RegistersHostedService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        services.AddSingleton(connectionMultiplexer);
        services.AddLogging();

        services.AddSecretManagement(configuration, environment);

        var hostedServiceDescriptor = services
            .FirstOrDefault(d => d.ImplementationType == typeof(SecretRotationService));

        hostedServiceDescriptor.Should().NotBeNull();
    }
}
