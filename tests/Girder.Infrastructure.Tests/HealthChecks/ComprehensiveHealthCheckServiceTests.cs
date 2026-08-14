using Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class ComprehensiveHealthCheckServiceTests
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComprehensiveHealthCheckService> _logger = Substitute.For<ILogger<ComprehensiveHealthCheckService>>();

    public ComprehensiveHealthCheckServiceTests()
    {
        var services = new ServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
    }

    private ComprehensiveHealthCheckService CreateService() =>
        new(_serviceProvider, _logger);

    #region Constructor Validation

    [Fact]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        var act = () => new ComprehensiveHealthCheckService(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ComprehensiveHealthCheckService(_serviceProvider, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region RegisterHealthCheck

    [Fact]
    public void RegisterHealthCheck_WithNullName_ThrowsArgumentException()
    {
        var service = CreateService();
        var act = () => service.RegisterHealthCheck(null!, () => Task.FromResult(HealthCheckResult.Healthy()));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterHealthCheck_WithEmptyName_ThrowsArgumentException()
    {
        var service = CreateService();
        var act = () => service.RegisterHealthCheck("", () => Task.FromResult(HealthCheckResult.Healthy()));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegisterHealthCheck_WithNullCheck_ThrowsArgumentNullException()
    {
        var service = CreateService();
        var act = () => service.RegisterHealthCheck("test", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterHealthCheck_ValidInput_DoesNotThrow()
    {
        var service = CreateService();
        var act = () => service.RegisterHealthCheck("test", () => Task.FromResult(HealthCheckResult.Healthy()));
        act.Should().NotThrow();
    }

    #endregion

    #region GetHealthReportAsync

    [Fact]
    public async Task GetHealthReportAsync_NoChecks_ReturnsHealthy()
    {
        var service = CreateService();

        var report = await service.GetHealthReportAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHealthReportAsync_AllHealthy_ReturnsHealthy()
    {
        var service = CreateService();
        service.RegisterHealthCheck("check1", () => Task.FromResult(HealthCheckResult.Healthy("ok")));
        service.RegisterHealthCheck("check2", () => Task.FromResult(HealthCheckResult.Healthy("ok")));

        var report = await service.GetHealthReportAsync();

        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Should().HaveCount(2);
        report.Entries.Should().ContainKey("check1");
        report.Entries.Should().ContainKey("check2");
    }

    [Fact]
    public async Task GetHealthReportAsync_OneDegraded_ReturnsDegraded()
    {
        var service = CreateService();
        service.RegisterHealthCheck("healthy", () => Task.FromResult(HealthCheckResult.Healthy()));
        service.RegisterHealthCheck("degraded", () => Task.FromResult(HealthCheckResult.Degraded("slow")));

        var report = await service.GetHealthReportAsync();

        report.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task GetHealthReportAsync_OneUnhealthy_ReturnsUnhealthy()
    {
        var service = CreateService();
        service.RegisterHealthCheck("healthy", () => Task.FromResult(HealthCheckResult.Healthy()));
        service.RegisterHealthCheck("unhealthy", () => Task.FromResult(HealthCheckResult.Unhealthy("down")));

        var report = await service.GetHealthReportAsync();

        report.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task GetHealthReportAsync_CheckThrows_ReturnsUnhealthy()
    {
        var service = CreateService();
        service.RegisterHealthCheck("throws", () => throw new InvalidOperationException("boom"));

        var report = await service.GetHealthReportAsync();

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries["throws"].Status.Should().Be(HealthStatus.Unhealthy);
    }

    #endregion

    #region CheckHealthAsync

    [Fact]
    public async Task CheckHealthAsync_NoChecks_ReturnsHealthy()
    {
        var service = CreateService();

        var result = await service.CheckHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Results.Should().BeEmpty();
        result.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CheckHealthAsync_WithChecks_ReturnsResults()
    {
        var service = CreateService();
        service.RegisterHealthCheck("ok", () => Task.FromResult(HealthCheckResult.Healthy()));

        var result = await service.CheckHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Results.Should().ContainKey("ok");
    }

    [Fact]
    public async Task CheckHealthAsync_MixedStatuses_ReturnsWorstStatus()
    {
        var service = CreateService();
        service.RegisterHealthCheck("healthy", () => Task.FromResult(HealthCheckResult.Healthy()));
        service.RegisterHealthCheck("degraded", () => Task.FromResult(HealthCheckResult.Degraded("slow")));
        service.RegisterHealthCheck("unhealthy", () => Task.FromResult(HealthCheckResult.Unhealthy("down")));

        var result = await service.CheckHealthAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_CheckThrows_SetsUnhealthy()
    {
        var service = CreateService();
        service.RegisterHealthCheck("throws", () => throw new Exception("test error"));

        var result = await service.CheckHealthAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Results["throws"].Status.Should().Be(HealthStatus.Unhealthy);
    }

    #endregion

    #region IsHealthyAsync

    [Fact]
    public async Task IsHealthyAsync_AllHealthy_ReturnsTrue()
    {
        var service = CreateService();
        service.RegisterHealthCheck("ok", () => Task.FromResult(HealthCheckResult.Healthy()));

        var result = await service.IsHealthyAsync();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsHealthyAsync_AnyUnhealthy_ReturnsFalse()
    {
        var service = CreateService();
        service.RegisterHealthCheck("bad", () => Task.FromResult(HealthCheckResult.Unhealthy("down")));

        var result = await service.IsHealthyAsync();

        result.Should().BeFalse();
    }

    #endregion

    #region CheckDatabaseHealthAsync / CheckRedisHealthAsync / CheckRabbitMQHealthAsync

    [Fact]
    public async Task CheckDatabaseHealthAsync_NoDbCheck_ReturnsHealthy()
    {
        var service = CreateService();

        var result = await service.CheckDatabaseHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("No database health check configured");
    }

    [Fact]
    public async Task CheckRedisHealthAsync_NoRedisCheck_ReturnsHealthy()
    {
        var service = CreateService();

        var result = await service.CheckRedisHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("No Redis health check configured");
    }

    [Fact]
    public async Task CheckRabbitMQHealthAsync_NoRabbitCheck_ReturnsHealthy()
    {
        var service = CreateService();

        var result = await service.CheckRabbitMQHealthAsync();

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("No RabbitMQ health check configured");
    }

    [Fact]
    public async Task CheckDatabaseHealthAsync_WithRegisteredCheck_WhenDbThrows_ReturnsUnhealthy()
    {
        // DatabaseHealthCheck is a concrete class with non-virtual methods — can't mock.
        // Register a real instance whose DbContext will throw on CanConnectAsync,
        // exercising the exception catch path in ComprehensiveHealthCheckService.
        var services = new ServiceCollection();
        var mockDbContext = Substitute.For<Microsoft.EntityFrameworkCore.DbContext>();
        var dbCheck = new DatabaseHealthCheck(mockDbContext, Substitute.For<ILogger<DatabaseHealthCheck>>());
        services.AddSingleton(dbCheck);
        var sp = services.BuildServiceProvider();

        var service = new ComprehensiveHealthCheckService(sp, _logger);
        var result = await service.CheckDatabaseHealthAsync();

        // The mock DbContext.Database.CanConnectAsync will throw (no real provider),
        // so ComprehensiveHealthCheckService catches it and returns Unhealthy
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckRedisHealthAsync_WithRegisteredCheck_WhenRedisThrows_ReturnsUnhealthy()
    {
        // RedisHealthCheck is concrete with non-virtual methods — can't mock.
        // Register a real instance whose multiplexer will throw on GetDatabase,
        // exercising the exception catch path.
        var services = new ServiceCollection();
        var mockMultiplexer = Substitute.For<StackExchange.Redis.IConnectionMultiplexer>();
        mockMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>())
            .Throws(new Exception("Redis unavailable"));
        var redisCheck = new RedisHealthCheck(mockMultiplexer, Substitute.For<ILogger<RedisHealthCheck>>());
        services.AddSingleton(redisCheck);
        var sp = services.BuildServiceProvider();

        var service = new ComprehensiveHealthCheckService(sp, _logger);
        var result = await service.CheckRedisHealthAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckRabbitMQHealthAsync_WithRegisteredCheck_WhenConnectionClosed_ReturnsUnhealthy()
    {
        var services = new ServiceCollection();
        var mockConnection = Substitute.For<RabbitMQ.Client.IConnection>();
        mockConnection.IsOpen.Returns(false);
        var rabbitCheck = new RabbitMQHealthCheck(mockConnection, Substitute.For<ILogger<RabbitMQHealthCheck>>());
        services.AddSingleton(rabbitCheck);
        var sp = services.BuildServiceProvider();

        var service = new ComprehensiveHealthCheckService(sp, _logger);
        var result = await service.CheckRabbitMQHealthAsync();

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    #endregion

    #region ComprehensiveHealthCheckResult DTO

    [Fact]
    public void ComprehensiveHealthCheckResult_DefaultValues()
    {
        var result = new ComprehensiveHealthCheckResult();

        result.Results.Should().NotBeNull();
        result.Results.Should().BeEmpty();
        result.TotalDuration.Should().Be(TimeSpan.Zero);
    }

    #endregion
}
