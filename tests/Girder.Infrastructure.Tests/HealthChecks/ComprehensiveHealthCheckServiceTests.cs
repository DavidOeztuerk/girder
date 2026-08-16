using Girder.Data.EntityFrameworkCore;
using Girder.Redis.HealthChecks;
using Girder.Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.HealthChecks;

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
