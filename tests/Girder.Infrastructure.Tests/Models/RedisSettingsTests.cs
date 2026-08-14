using Girder.Infrastructure.Models;

namespace Girder.Infrastructure.Tests.Models;

[Trait("Category", "Unit")]
public class RedisSettingsTests
{
    [Fact]
    public void RedisSettings_DefaultConstruction_HasExpectedDefaults()
    {
        var settings = new RedisSettings();

        settings.ConnectionString.Should().Be("localhost:6379");
        settings.InstanceName.Should().Be("girder");
        settings.DefaultExpirationMinutes.Should().Be(60);
        settings.EnableDistributedCache.Should().BeTrue();
        settings.RetryCount.Should().Be(3);
        settings.RetryDelayMilliseconds.Should().Be(1000);
    }

    [Fact]
    public void RedisSettings_PropertyAssignment_Works()
    {
        var settings = new RedisSettings
        {
            ConnectionString = "redis-server:6380",
            InstanceName = "myapp",
            DefaultExpirationMinutes = 30,
            EnableDistributedCache = false,
            RetryCount = 5,
            RetryDelayMilliseconds = 500
        };

        settings.ConnectionString.Should().Be("redis-server:6380");
        settings.InstanceName.Should().Be("myapp");
        settings.DefaultExpirationMinutes.Should().Be(30);
        settings.EnableDistributedCache.Should().BeFalse();
        settings.RetryCount.Should().Be(5);
        settings.RetryDelayMilliseconds.Should().Be(500);
    }

    [Fact]
    public void RedisSettings_ConnectionString_CanBeEmpty()
    {
        var settings = new RedisSettings { ConnectionString = string.Empty };
        settings.ConnectionString.Should().Be(string.Empty);
    }
}
