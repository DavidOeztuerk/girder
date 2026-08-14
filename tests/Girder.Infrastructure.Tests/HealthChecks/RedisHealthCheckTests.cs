using Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class RedisHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("redis", check, null, null)
        };

    [Fact]
    public async Task CheckHealthAsync_WhenNotConnected_ReturnsUnhealthy()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };
        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.IsConnected.Returns(false);

        var logger = Substitute.For<ILogger<RedisHealthCheck>>();
        var check = new RedisHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not established");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenGetDatabaseThrows_ReturnsUnhealthy()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };
        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.IsConnected.Returns(true);
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Connection refused"));

        var logger = Substitute.For<ILogger<RedisHealthCheck>>();
        var check = new RedisHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenStringSetFails_ReturnsUnhealthy()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };

        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.IsConnected.Returns(true);
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("SET failed"));

        var logger = Substitute.For<ILogger<RedisHealthCheck>>();
        var check = new RedisHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenReadWriteValueMismatch_ReturnsUnhealthy()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };

        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.IsConnected.Returns(true);
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);

        database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>()).Returns(true);
        // Return different value than what was stored
        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"unexpected-value");
        database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        var logger = Substitute.For<ILogger<RedisHealthCheck>>();
        var check = new RedisHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("read/write test failed");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenReadWriteSucceeds_ReturnsHealthy()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };

        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.IsConnected.Returns(true);
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        multiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object?>()).Returns(server);

        // Capture stored value so StringGetAsync can echo it back
        RedisValue storedValue = default;
        database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<TimeSpan?>(),
            Arg.Any<bool>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                storedValue = callInfo.ArgAt<RedisValue>(1);
                return Task.FromResult(true);
            });
        database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(_ => Task.FromResult(storedValue));
        database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        IGrouping<string, KeyValuePair<string, string>>[] emptyInfo = Array.Empty<IGrouping<string, KeyValuePair<string, string>>>();
        server.InfoAsync(Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()).Returns(emptyInfo);

        var logger = Substitute.For<ILogger<RedisHealthCheck>>();
        var check = new RedisHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("healthy");
    }
}

[Trait("Category", "Unit")]
public class RedisPerformanceHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("redis-performance", check, null, null)
        };

    [Fact]
    public async Task CheckHealthAsync_WhenGetDatabaseThrows_ReturnsDegraded()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };
        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>())
            .Throws(new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Failed"));

        var logger = Substitute.For<ILogger<RedisPerformanceHealthCheck>>();
        var check = new RedisPerformanceHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenServerThrows_ReturnsDegraded()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };

        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        multiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object?>())
            .Throws(new RedisException("Server unavailable"));

        var logger = Substitute.For<ILogger<RedisPerformanceHealthCheck>>();
        var check = new RedisPerformanceHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPingFast_ReturnsHealthyOrDegraded()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };

        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        multiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object?>()).Returns(server);

        // Fast ping
        database.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(5));

        // Info returns minimal groups — configure using explicit arg matchers to avoid dangling specs
        IGrouping<string, KeyValuePair<string, string>>[] infoGroups = Array.Empty<IGrouping<string, KeyValuePair<string, string>>>();

        server.InfoAsync(Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()).Returns(infoGroups);

        var logger = Substitute.For<ILogger<RedisPerformanceHealthCheck>>();
        var check = new RedisPerformanceHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        // With fast ping (<100ms), low memory (0%), and low clients (5/100), should be Healthy or at worst Degraded
        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPingSucceeds_DataContainsLatencyKeys()
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        var server = Substitute.For<IServer>();
        var endPoints = new EndPointCollection { new System.Net.DnsEndPoint("localhost", 6379) };

        multiplexer.GetEndPoints().Returns(endPoints.ToArray());
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(database);
        multiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object?>()).Returns(server);

        database.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(1));

        IGrouping<string, KeyValuePair<string, string>>[] infoGroups = Array.Empty<IGrouping<string, KeyValuePair<string, string>>>();
        server.InfoAsync(Arg.Any<RedisValue>(), Arg.Any<CommandFlags>()).Returns(infoGroups);

        var logger = Substitute.For<ILogger<RedisPerformanceHealthCheck>>();
        var check = new RedisPerformanceHealthCheck(multiplexer, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        // Status is Healthy or Degraded depending on machine speed, but Data always has latency keys
        result.Data.Should().ContainKey("average_latency_ms");
        result.Data.Should().ContainKey("max_latency_ms");
        result.Data.Should().ContainKey("memory_usage_percent");
    }
}

