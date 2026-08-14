using Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class RabbitMQHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("rabbitmq", check, null, null)
        };

    [Fact]
    public void Constructor_WithNullConnection_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<RabbitMQHealthCheck>>();
        var act = () => new RabbitMQHealthCheck(null!, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var connection = Substitute.For<IConnection>();
        var act = () => new RabbitMQHealthCheck(connection, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionClosed_ReturnsUnhealthy()
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(false);
        var logger = Substitute.For<ILogger<RabbitMQHealthCheck>>();
        var check = new RabbitMQHealthCheck(connection, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("connection is closed");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionOpenAndChannelOpen_ReturnsHealthy()
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);

        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(channel);

        var logger = Substitute.For<ILogger<RabbitMQHealthCheck>>();
        var check = new RabbitMQHealthCheck(connection, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("accessible");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenChannelNotOpen_ReturnsUnhealthy()
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);

        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(false);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(channel);

        var logger = Substitute.For<ILogger<RabbitMQHealthCheck>>();
        var check = new RabbitMQHealthCheck(connection, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("channel could not be opened");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_ReturnsUnhealthy()
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("RabbitMQ error"));

        var logger = Substitute.For<ILogger<RabbitMQHealthCheck>>();
        var check = new RabbitMQHealthCheck(connection, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenHealthy_ReturnsDataWithResponseTime()
    {
        var connection = Substitute.For<IConnection>();
        connection.IsOpen.Returns(true);

        var channel = Substitute.For<IChannel>();
        channel.IsOpen.Returns(true);
        connection.CreateChannelAsync(Arg.Any<CreateChannelOptions?>(), Arg.Any<CancellationToken>())
            .Returns(channel);

        var logger = Substitute.For<ILogger<RabbitMQHealthCheck>>();
        var check = new RabbitMQHealthCheck(connection, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("responseTime");
        result.Data.Should().ContainKey("isOpen");
        result.Data.Should().ContainKey("channelOpen");
    }
}
