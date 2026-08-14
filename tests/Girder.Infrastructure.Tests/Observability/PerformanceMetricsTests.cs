using Girder.Infrastructure.Observability;

namespace Girder.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class PerformanceMetricsTests
{
    private readonly PerformanceMetrics _metrics = new();

    [Fact]
    public void RecordRequest_ShouldNotThrow()
    {
        var act = () => _metrics.RecordRequest("GET", "/api/test", 200, 50.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRequest_WithErrorStatusCode_ShouldNotThrow()
    {
        var act = () => _metrics.RecordRequest("POST", "/api/test", 500, 100.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRequest_With400StatusCode_ShouldNotThrow()
    {
        var act = () => _metrics.RecordRequest("PUT", "/api/test", 400, 30.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void GetRequestMetricsSnapshot_ReturnsDailyRequestSummary()
    {
        var metrics = new PerformanceMetrics();

        metrics.RecordRequest("GET", "/api/test", 200, 100.0);
        metrics.RecordRequest("POST", "/api/test", 500, 300.0);

        var snapshot = metrics.GetRequestMetricsSnapshot(DateTime.UtcNow);

        snapshot.TotalRequestsToday.Should().Be(2);
        snapshot.TotalErrorsToday.Should().Be(1);
        snapshot.ErrorRatePercent.Should().Be(50);
        snapshot.AverageResponseTimeMs.Should().Be(200);
    }

    [Fact]
    public void RecordDatabaseQuery_Success_ShouldNotThrow()
    {
        var act = () => _metrics.RecordDatabaseQuery("SELECT", 10.0, true);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordDatabaseQuery_Failure_ShouldNotThrow()
    {
        var act = () => _metrics.RecordDatabaseQuery("INSERT", 50.0, false);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCacheOperation_Hit_ShouldNotThrow()
    {
        var act = () => _metrics.RecordCacheOperation("GET", true, 1.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCacheOperation_Miss_ShouldNotThrow()
    {
        var act = () => _metrics.RecordCacheOperation("GET", false, 2.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordUserAction_ShouldNotThrow()
    {
        var act = () => _metrics.RecordUserAction("login", "user-123");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordSkillManagement_ShouldNotThrow()
    {
        var act = () => _metrics.RecordSkillManagement("create", "programming");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordMatchProcessing_ShouldNotThrow()
    {
        var act = () => _metrics.RecordMatchProcessing("create", "automatic");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordRateLimitExceeded_ShouldNotThrow()
    {
        var act = () => _metrics.RecordRateLimitExceeded("ip", "/api/test", 5.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCircuitBreakerStateChange_ShouldNotThrow()
    {
        var act = () => _metrics.RecordCircuitBreakerStateChange("UserService", "Closed", "Open");
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordCircuitBreakerOperation_ShouldNotThrow()
    {
        var act = () => _metrics.RecordCircuitBreakerOperation("UserService", true, 50.0);
        act.Should().NotThrow();
    }
}
