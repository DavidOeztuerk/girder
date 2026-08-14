using Girder.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Girder.Infrastructure.Tests.Configuration;

[Trait("Category", "Unit")]
[Collection("EnvironmentVariables")]
public class ConfigurationHelperTests
{
    private static readonly string[] EnvVarsToClean =
    [
        "POSTGRES_HOST",
        "POSTGRES_PORT",
        "POSTGRES_DB",
        "POSTGRES_USER",
        "POSTGRES_PASSWORD",
        "REDIS_CONNECTION",
        "RABBITMQ_HOST",
        "RABBITMQ_PORT",
        "RABBITMQ_USER",
        "RABBITMQ_PASSWORD",
        "RABBITMQ_VHOST"
    ];

    private static void ClearEnvVars()
    {
        foreach (var name in EnvVarsToClean)
            Environment.SetEnvironmentVariable(name, null);
    }

    private static IConfiguration BuildInMemoryConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    #region GetConnectionString — DefaultConnection from config

    [Fact]
    public void GetConnectionString_DefaultConnection_FromConfig_ReturnsValue()
    {
        var config = BuildInMemoryConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=db;Database=test;Password=pass"
        });

        var result = ConfigurationHelper.GetConnectionString(config);

        result.Should().Be("Host=db;Database=test;Password=pass");
    }

    [Fact]
    public void GetConnectionString_NamedConnection_FromConfig_ReturnsValue()
    {
        var config = BuildInMemoryConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "redis-server:6379"
        });

        var result = ConfigurationHelper.GetConnectionString(config, "Redis");

        result.Should().Be("redis-server:6379");
    }

    #endregion

    #region GetConnectionString — DefaultConnection from env vars

    [Fact]
    public void GetConnectionString_DefaultConnection_NoConfig_BuildsFromEnvVars()
    {
        ClearEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_HOST", "envhost");
            Environment.SetEnvironmentVariable("POSTGRES_PORT", "5433");
            Environment.SetEnvironmentVariable("POSTGRES_DB", "envdb");
            Environment.SetEnvironmentVariable("POSTGRES_USER", "envuser");
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "envpass");

            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var result = ConfigurationHelper.GetConnectionString(config);

            result.Should().Contain("Host=envhost");
            result.Should().Contain("Port=5433");
            result.Should().Contain("Database=envdb");
            result.Should().Contain("Username=envuser");
            result.Should().Contain("Password=envpass");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_DefaultConnection_NoConfig_DefaultsForHostPortDbUser()
    {
        ClearEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "requiredpass");

            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var result = ConfigurationHelper.GetConnectionString(config);

            result.Should().Contain("Host=localhost");
            result.Should().Contain("Port=5432");
            result.Should().Contain("Database=girder");
            result.Should().Contain("Username=girder");
            result.Should().Contain("Password=requiredpass");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_DefaultConnection_NoConfig_NoPassword_Throws()
    {
        ClearEnvVars();
        try
        {
            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var act = () => ConfigurationHelper.GetConnectionString(config);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*POSTGRES_PASSWORD*");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Redis from env var

    [Fact]
    public void GetConnectionString_Redis_NoConfig_ReadsFromEnvVar()
    {
        ClearEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("REDIS_CONNECTION", "redis-host:6380");

            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var result = ConfigurationHelper.GetConnectionString(config, "Redis");

            result.Should().Be("redis-host:6380");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_Redis_NoConfig_NoEnvVar_Throws()
    {
        ClearEnvVars();
        try
        {
            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var act = () => ConfigurationHelper.GetConnectionString(config, "Redis");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*Redis*not configured*");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — RabbitMQ from env vars

    [Fact]
    public void GetConnectionString_RabbitMQ_NoConfig_BuildsFromEnvVars()
    {
        ClearEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("RABBITMQ_HOST", "rmqhost");
            Environment.SetEnvironmentVariable("RABBITMQ_PORT", "5673");
            Environment.SetEnvironmentVariable("RABBITMQ_USER", "rmquser");
            Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "rmqpass");
            Environment.SetEnvironmentVariable("RABBITMQ_VHOST", "/myvhost");

            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var result = ConfigurationHelper.GetConnectionString(config, "RabbitMQ");

            result.Should().Be("amqp://rmquser:rmqpass@rmqhost:5673/myvhost");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_RabbitMQ_NoConfig_DefaultsForAllFields()
    {
        ClearEnvVars();
        try
        {
            var config = BuildInMemoryConfig(new Dictionary<string, string?>());

            var result = ConfigurationHelper.GetConnectionString(config, "RabbitMQ");

            result.Should().Be("amqp://guest:guest@localhost:5672/");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Unknown name

    [Fact]
    public void GetConnectionString_UnknownName_NoConfig_Throws()
    {
        var config = BuildInMemoryConfig(new Dictionary<string, string?>());

        var act = () => ConfigurationHelper.GetConnectionString(config, "UnknownService");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown connection string*UnknownService*");
    }

    #endregion

    #region GetConnectionString — Config takes precedence over env-var fallback

    [Fact]
    public void GetConnectionString_ConfigTakesPrecedenceOverEnvVarFallback()
    {
        ClearEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "envpass");

            var config = BuildInMemoryConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=confighost;Password=configpass"
            });

            var result = ConfigurationHelper.GetConnectionString(config);

            result.Should().Be("Host=confighost;Password=configpass");
        }
        finally
        {
            ClearEnvVars();
        }
    }

    #endregion

    #region GetRequiredConfiguration

    [Fact]
    public void GetRequiredConfiguration_BindsSection()
    {
        var config = BuildInMemoryConfig(new Dictionary<string, string?>
        {
            ["TestSection:Name"] = "TestValue",
            ["TestSection:Count"] = "42"
        });

        var result = ConfigurationHelper.GetRequiredConfiguration<TestConfig>(config, "TestSection");

        result.Name.Should().Be("TestValue");
        result.Count.Should().Be(42);
    }

    [Fact]
    public void GetRequiredConfiguration_MissingSection_ReturnsDefaultValues()
    {
        var config = BuildInMemoryConfig(new Dictionary<string, string?>());

        var result = ConfigurationHelper.GetRequiredConfiguration<TestConfig>(config, "NonExistent");

        result.Should().NotBeNull();
        result.Name.Should().BeNull();
        result.Count.Should().Be(0);
    }

    private class TestConfig
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    #endregion
}
