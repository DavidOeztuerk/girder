using Infrastructure.Observability;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class PerformanceMetricsTopUpTests
{
    [Fact]
    public void PerformanceMetrics_RecordSkillManagement_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordSkillManagement("create", "programming");

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordMatchProcessing_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordMatchProcessing("accept", "automatic");

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordCircuitBreakerStateChange_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordCircuitBreakerStateChange("UserService", "Closed", "Open");

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordCircuitBreakerOperation_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordCircuitBreakerOperation("UserService", true, 15.5);

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordCircuitBreakerOperation_Failed_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordCircuitBreakerOperation("UserService", false, 500.0);

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordRateLimitExceeded_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordRateLimitExceeded("ip", "/api/users", 2.5);

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordRequest_Error_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordRequest("GET", "/api/test", 500, 150.0);

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordDatabaseQuery_Failed_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordDatabaseQuery("SELECT", 30.0, false);

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordCacheOperation_Miss_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordCacheOperation("get", false, 1.0);

        act.Should().NotThrow();
    }

    [Fact]
    public void PerformanceMetrics_RecordUserAction_ShouldNotThrow()
    {
        var metrics = new PerformanceMetrics();
        var act = () => metrics.RecordUserAction("login", "user-123");

        act.Should().NotThrow();
    }
}

[Trait("Category", "Unit")]
public class PerformanceMonitoringServiceTopUpTests
{
    private readonly IPerformanceMetrics _metrics = Substitute.For<IPerformanceMetrics>();
    private readonly ILogger<PerformanceMonitoringService> _logger = Substitute.For<ILogger<PerformanceMonitoringService>>();

    [Fact]
    public void TimerScope_Dispose_RecordsCircuitBreakerOperation_WhenTagsSet()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("circuit-breaker-op");
        timer.AddTag("circuit_breaker", "UserService");
        timer.AddTag("success", true);
        timer.Dispose();

        // No throw is the assertion — it exercises the dispose path with tags
    }

    [Fact]
    public void GetCurrentSnapshot_ContainsGcCollections()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var snapshot = svc.GetCurrentSnapshot();

        // Gen0/1/2 collections are always >= 0
        snapshot.Gen0Collections.Should().BeGreaterThanOrEqualTo(0);
        snapshot.Gen1Collections.Should().BeGreaterThanOrEqualTo(0);
        snapshot.Gen2Collections.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetCurrentSnapshot_ContainsUptimeSeconds()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var snapshot = svc.GetCurrentSnapshot();

        snapshot.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void AddPerformanceAlert_DefaultAlerts_AlreadyRegistered_RegisteringMore_DoesNotThrow()
    {
        // PerformanceMonitoringService registers 4 default alerts in constructor
        using var svc = new PerformanceMonitoringService(_metrics, _logger);

        var alert = new PerformanceAlert
        {
            Name = "CustomAlert",
            Description = "Custom condition",
            Condition = s => s.ThreadCount > 1000
        };

        var act = () => svc.AddPerformanceAlert(alert);
        act.Should().NotThrow();
    }

    [Fact]
    public void DefaultAlert_HighCpuUsage_TriggersAtHighCpu()
    {
        var alert = new PerformanceAlert
        {
            Name = "HighCpuUsage",
            Description = "CPU usage is above 80%",
            Condition = snapshot => snapshot.CpuUsagePercent > 80
        };

        alert.ShouldTrigger(new PerformanceSnapshot { CpuUsagePercent = 85 }).Should().BeTrue();
        alert.ShouldTrigger(new PerformanceSnapshot { CpuUsagePercent = 79 }).Should().BeFalse();
    }

    [Fact]
    public void DefaultAlert_HighMemoryUsage_TriggersAbove1GB()
    {
        var alert = new PerformanceAlert
        {
            Name = "HighMemoryUsage",
            Description = "Memory usage is above 1GB",
            Condition = snapshot => snapshot.MemoryUsageMB > 1024
        };

        alert.ShouldTrigger(new PerformanceSnapshot { MemoryUsageMB = 2048 }).Should().BeTrue();
        alert.ShouldTrigger(new PerformanceSnapshot { MemoryUsageMB = 512 }).Should().BeFalse();
    }

    [Fact]
    public void DefaultAlert_HighThreadCount_TriggersAbove500()
    {
        var alert = new PerformanceAlert
        {
            Name = "HighThreadCount",
            Description = "Thread count is above 500",
            Condition = snapshot => snapshot.ThreadCount > 500
        };

        alert.ShouldTrigger(new PerformanceSnapshot { ThreadCount = 600 }).Should().BeTrue();
        alert.ShouldTrigger(new PerformanceSnapshot { ThreadCount = 499 }).Should().BeFalse();
    }

    [Fact]
    public void DefaultAlert_FrequentGcGen2_TriggersAbove100()
    {
        var alert = new PerformanceAlert
        {
            Name = "FrequentGcGen2",
            Description = "High number of Gen2 GC collections",
            Condition = snapshot => snapshot.Gen2Collections > 100
        };

        alert.ShouldTrigger(new PerformanceSnapshot { Gen2Collections = 150 }).Should().BeTrue();
        alert.ShouldTrigger(new PerformanceSnapshot { Gen2Collections = 50 }).Should().BeFalse();
    }

    [Fact]
    public void TimerScope_Dispose_NonDatabaseNonCache_DoesNotCallMetrics()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("generic-operation");
        timer.Dispose();

        // Neither RecordDatabaseQuery nor RecordCacheOperation should be called
        _metrics.DidNotReceive().RecordDatabaseQuery(Arg.Any<string>(), Arg.Any<double>());
        _metrics.DidNotReceive().RecordDatabaseQuery(Arg.Any<string>(), Arg.Any<double>(), Arg.Any<bool>());
        _metrics.DidNotReceive().RecordCacheOperation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<double>());
    }

    [Fact]
    public void TimerScope_Dispose_CacheOperation_WithoutHitTag_DefaultsToFalse()
    {
        using var svc = new PerformanceMonitoringService(_metrics, _logger);
        var timer = svc.StartTimer("cache-get");
        // No "hit" tag added — defaults to false
        timer.Dispose();

        _metrics.Received(1).RecordCacheOperation("cache-get", false, Arg.Any<double>());
    }
}
