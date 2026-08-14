using Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Tests.Logging;

[Trait("Category", "Unit")]
public class LoggingConfigurationTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    // Custom IHostEnvironment that properly implements extension methods by setting EnvironmentName
    private static IHostEnvironment CreateEnv(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    [Fact]
    public void ConfigureSerilog_DevelopmentEnvironment_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var env = CreateEnv(Environments.Development);

        var act = () => LoggingConfiguration.ConfigureSerilog(config, env, "TestService");

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureSerilog_ProductionEnvironment_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var env = CreateEnv(Environments.Production);

        var act = () => LoggingConfiguration.ConfigureSerilog(config, env, "TestService");

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureSerilog_StagingEnvironment_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var env = CreateEnv(Environments.Staging);

        var act = () => LoggingConfiguration.ConfigureSerilog(config, env, "TestService");

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureSerilog_WithElasticsearchConnectionString_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Elasticsearch"] = "http://localhost:9200"
        });
        var env = CreateEnv(Environments.Production);

        var act = () => LoggingConfiguration.ConfigureSerilog(config, env, "TestService");

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigureSerilog_WithDifferentServiceName_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var env = CreateEnv(Environments.Development);

        var act = () => LoggingConfiguration.ConfigureSerilog(config, env, "UserService");

        act.Should().NotThrow();
    }
}
