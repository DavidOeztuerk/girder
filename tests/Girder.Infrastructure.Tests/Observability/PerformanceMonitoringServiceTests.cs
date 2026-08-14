using Girder.Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class PerformanceMonitoringServiceTests
{
    private readonly IPerformanceMetrics _metrics = Substitute.For<IPerformanceMetrics>();
    private readonly ILogger<PerformanceMonitoringService> _logger = Substitute.For<ILogger<PerformanceMonitoringService>>();

    [Fact]
    public void StartTimer_ShouldReturnTimerScope()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("test-op");

        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<ITimerScope>();
        timer.Dispose();
    }

    [Fact]
    public void RecordCustomMetric_ShouldNotThrow()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var act = () => svc.RecordCustomMetric("test", 42.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void AddPerformanceAlert_ShouldStoreAlert()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var alert = new PerformanceAlert
        {
            Name = "TestAlert",
            Description = "Test alert description",
            Condition = _ => false
        };

        var act = () => svc.AddPerformanceAlert(alert);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetCurrentSnapshot_ShouldReturnValidSnapshot()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var snapshot = svc.GetCurrentSnapshot();

        snapshot.Should().NotBeNull();
        snapshot.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        snapshot.MemoryUsageMB.Should().BeGreaterThan(0);
        snapshot.ThreadCount.Should().BeGreaterThan(0);
        snapshot.GcTotalMemoryMB.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        var svc = new PerformanceMonitoringService(_metrics, _logger);
        var act = () => svc.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void TimerScope_AddTag_ShouldNotThrow()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("test-op");
        var act = () => timer.AddTag("key", "value");
        act.Should().NotThrow();
        timer.Dispose();
    }

    [Fact]
    public void TimerScope_Dispose_DatabaseOperation_ShouldRecordDatabaseQuery()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("database-query");
        timer.Dispose();

        _metrics.Received(1).RecordDatabaseQuery(
            "database-query",
            Arg.Any<double>());
    }

    [Fact]
    public void TimerScope_Dispose_CacheOperation_WithHitTag_ShouldRecordCacheHit()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("cache-lookup");
        timer.AddTag("hit", true);
        timer.Dispose();

        _metrics.Received(1).RecordCacheOperation(
            "cache-lookup",
            true,
            Arg.Any<double>());
    }

    [Fact]
    public void TimerScope_Dispose_CacheOperation_WithMiss_ShouldRecordCacheMiss()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("cache-lookup");
        timer.AddTag("hit", false);
        timer.Dispose();

        _metrics.Received(1).RecordCacheOperation(
            "cache-lookup",
            false,
            Arg.Any<double>());
    }

    [Fact]
    public void PerformanceAlert_ShouldTrigger_WhenConditionMet()
    {
        var alert = new PerformanceAlert
        {
            Name = "HighCpu",
            Description = "CPU > 80%",
            Condition = s => s.CpuUsagePercent > 80
        };

        var snapshot = new PerformanceSnapshot { CpuUsagePercent = 90 };
        alert.ShouldTrigger(snapshot).Should().BeTrue();
    }

    [Fact]
    public void PerformanceAlert_ShouldNotTrigger_WhenConditionNotMet()
    {
        var alert = new PerformanceAlert
        {
            Name = "HighCpu",
            Description = "CPU > 80%",
            Condition = s => s.CpuUsagePercent > 80
        };

        var snapshot = new PerformanceSnapshot { CpuUsagePercent = 50 };
        alert.ShouldTrigger(snapshot).Should().BeFalse();
    }

    [Fact]
    public void PerformanceSnapshot_DefaultValues_ShouldBeZero()
    {
        var snapshot = new PerformanceSnapshot();
        snapshot.CpuUsagePercent.Should().Be(0);
        snapshot.MemoryUsageMB.Should().Be(0);
        snapshot.ThreadCount.Should().Be(0);
        snapshot.GcTotalMemoryMB.Should().Be(0);
        snapshot.Gen0Collections.Should().Be(0);
        snapshot.Gen1Collections.Should().Be(0);
        snapshot.Gen2Collections.Should().Be(0);
        snapshot.UptimeSeconds.Should().Be(0);
    }
}
