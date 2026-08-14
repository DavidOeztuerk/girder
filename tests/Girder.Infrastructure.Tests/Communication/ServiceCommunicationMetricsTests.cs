using Infrastructure.Communication.Telemetry;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class ServiceCommunicationMetricsTests
{
    private readonly ILogger<ServiceCommunicationMetrics> _logger = Substitute.For<ILogger<ServiceCommunicationMetrics>>();

    private ServiceCommunicationMetrics CreateMetrics() => new(_logger);

    #region RecordServiceCall

    [Fact]
    public void RecordServiceCall_Success_IncrementsTotalAndSuccessful()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCall("UserService", "/api/users", "GET", 200, TimeSpan.FromMilliseconds(50));

        var summary = metrics.GetMetricsSummary();
        summary.TotalRequests.Should().Be(1);
        summary.SuccessfulRequests.Should().Be(1);
        summary.FailedRequests.Should().Be(0);
    }

    [Fact]
    public void RecordServiceCall_Failure_IncrementsTotalAndFailed()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCall("UserService", "/api/users", "GET", 500, TimeSpan.FromMilliseconds(100));

        var summary = metrics.GetMetricsSummary();
        summary.TotalRequests.Should().Be(1);
        summary.SuccessfulRequests.Should().Be(0);
        summary.FailedRequests.Should().Be(1);
    }

    [Fact]
    public void RecordServiceCall_FromCache_IncrementsCached()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCall("UserService", "/api/users", "GET", 200, TimeSpan.FromMilliseconds(1), fromCache: true);

        var summary = metrics.GetMetricsSummary();
        summary.CachedRequests.Should().Be(1);
    }

    [Fact]
    public void RecordServiceCall_TracksStatusCodeDistribution()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCall("svc", "/api", "GET", 200, TimeSpan.FromMilliseconds(10));
        metrics.RecordServiceCall("svc", "/api", "GET", 200, TimeSpan.FromMilliseconds(10));
        metrics.RecordServiceCall("svc", "/api", "POST", 201, TimeSpan.FromMilliseconds(20));
        metrics.RecordServiceCall("svc", "/api", "GET", 404, TimeSpan.FromMilliseconds(5));

        var summary = metrics.GetMetricsSummary();
        summary.StatusCodeDistribution["200"].Should().Be(2);
        summary.StatusCodeDistribution["201"].Should().Be(1);
        summary.StatusCodeDistribution["404"].Should().Be(1);
    }

    [Fact]
    public void RecordServiceCall_TracksResponseTimes()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCall("svc", "/api", "GET", 200, TimeSpan.FromMilliseconds(100));
        metrics.RecordServiceCall("svc", "/api", "GET", 200, TimeSpan.FromMilliseconds(200));

        var summary = metrics.GetMetricsSummary();
        summary.AverageResponseTime.Should().Be(150);
    }

    #endregion

    #region RecordServiceCallFailure

    [Fact]
    public void RecordServiceCallFailure_IncrementsTotalAndFailed()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCallFailure("UserService", "/api/users", "GET", "HttpRequestException", TimeSpan.FromMilliseconds(1000));

        var summary = metrics.GetMetricsSummary();
        summary.TotalRequests.Should().Be(1);
        summary.FailedRequests.Should().Be(1);
    }

    [Fact]
    public void RecordServiceCallFailure_TracksErrorTypeDistribution()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCallFailure("svc", "/api", "GET", "HttpRequestException", TimeSpan.FromMilliseconds(100));
        metrics.RecordServiceCallFailure("svc", "/api", "GET", "TimeoutException", TimeSpan.FromMilliseconds(5000));
        metrics.RecordServiceCallFailure("svc", "/api", "GET", "HttpRequestException", TimeSpan.FromMilliseconds(200));

        var summary = metrics.GetMetricsSummary();
        summary.ErrorTypeDistribution["HttpRequestException"].Should().Be(2);
        summary.ErrorTypeDistribution["TimeoutException"].Should().Be(1);
    }

    #endregion

    #region RecordRetryAttempt

    [Fact]
    public void RecordRetryAttempt_IncrementsRetryAttempts()
    {
        var metrics = CreateMetrics();

        metrics.RecordRetryAttempt("UserService", 1);
        metrics.RecordRetryAttempt("UserService", 2);

        var summary = metrics.GetMetricsSummary();
        summary.RetryAttempts.Should().Be(2);
    }

    #endregion

    #region RecordCircuitBreakerStateChange

    [Fact]
    public void RecordCircuitBreakerStateChange_UpdatesServiceMetrics()
    {
        var metrics = CreateMetrics();

        // Must have at least one call to create the service entry
        metrics.RecordCircuitBreakerStateChange("UserService", "Open");

        var serviceMetrics = metrics.GetServiceMetrics("userservice");
        serviceMetrics.Should().NotBeNull();
        serviceMetrics!.CircuitBreakerState.Should().Be("Open");
    }

    #endregion

    #region RecordCacheOperation / RecordTokenOperation

    [Fact]
    public void RecordCacheOperation_DoesNotThrow()
    {
        var metrics = CreateMetrics();
        var act = () => metrics.RecordCacheOperation("svc", "GET", true);
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordTokenOperation_DoesNotThrow()
    {
        var metrics = CreateMetrics();
        var act = () => metrics.RecordTokenOperation("refresh", true, TimeSpan.FromMilliseconds(100));
        act.Should().NotThrow();
    }

    #endregion

    #region GetMetricsSummary

    [Fact]
    public void GetMetricsSummary_Initial_AllZeros()
    {
        var metrics = CreateMetrics();

        var summary = metrics.GetMetricsSummary();

        summary.TotalRequests.Should().Be(0);
        summary.SuccessfulRequests.Should().Be(0);
        summary.FailedRequests.Should().Be(0);
        summary.CachedRequests.Should().Be(0);
        summary.RetryAttempts.Should().Be(0);
        summary.AverageResponseTime.Should().Be(0);
    }

    [Fact]
    public void GetMetricsSummary_IncludesServiceMetrics()
    {
        var metrics = CreateMetrics();

        metrics.RecordServiceCall("UserService", "/api/users", "GET", 200, TimeSpan.FromMilliseconds(50));
        metrics.RecordServiceCall("SkillService", "/api/skills", "GET", 200, TimeSpan.FromMilliseconds(30));

        var summary = metrics.GetMetricsSummary();
        summary.ServiceMetrics.Should().HaveCount(2);
        summary.ServiceMetrics.Should().ContainKey("userservice");
        summary.ServiceMetrics.Should().ContainKey("skillservice");
    }

    #endregion

    #region GetServiceMetrics

    [Fact]
    public void GetServiceMetrics_UnknownService_ReturnsNull()
    {
        var metrics = CreateMetrics();

        var result = metrics.GetServiceMetrics("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void GetServiceMetrics_ExistingService_ReturnsMetrics()
    {
        var metrics = CreateMetrics();
        metrics.RecordServiceCall("UserService", "/api/users", "GET", 200, TimeSpan.FromMilliseconds(50));

        var result = metrics.GetServiceMetrics("UserService");

        result.Should().NotBeNull();
        result!.TotalRequests.Should().Be(1);
        result.SuccessfulRequests.Should().Be(1);
        result.ServiceName.Should().Be("UserService");
    }

    [Fact]
    public void GetServiceMetrics_TracksEndpointMetrics()
    {
        var metrics = CreateMetrics();
        metrics.RecordServiceCall("svc", "/api/users", "GET", 200, TimeSpan.FromMilliseconds(50));
        metrics.RecordServiceCall("svc", "/api/users", "POST", 201, TimeSpan.FromMilliseconds(80));

        var result = metrics.GetServiceMetrics("svc");

        result.Should().NotBeNull();
        result!.EndpointMetrics.Should().HaveCount(2);
        result.EndpointMetrics.Should().ContainKey("GET:/api/users");
        result.EndpointMetrics.Should().ContainKey("POST:/api/users");
    }

    [Fact]
    public void GetServiceMetrics_TracksPercentiles()
    {
        var metrics = CreateMetrics();
        for (int i = 1; i <= 100; i++)
        {
            metrics.RecordServiceCall("svc", "/api", "GET", 200, TimeSpan.FromMilliseconds(i));
        }

        var result = metrics.GetServiceMetrics("svc");

        result.Should().NotBeNull();
        result!.P50ResponseTime.Should().BeGreaterThan(0);
        result.P95ResponseTime.Should().BeGreaterThan(result.P50ResponseTime);
        result.P99ResponseTime.Should().BeGreaterThanOrEqualTo(result.P95ResponseTime);
    }

    #endregion

    #region ResetMetrics

    [Fact]
    public void ResetMetrics_ClearsAllData()
    {
        var metrics = CreateMetrics();
        metrics.RecordServiceCall("svc", "/api", "GET", 200, TimeSpan.FromMilliseconds(50));
        metrics.RecordServiceCallFailure("svc", "/api", "POST", "Error", TimeSpan.FromMilliseconds(100));
        metrics.RecordRetryAttempt("svc", 1);

        metrics.ResetMetrics();

        var summary = metrics.GetMetricsSummary();
        summary.TotalRequests.Should().Be(0);
        summary.SuccessfulRequests.Should().Be(0);
        summary.FailedRequests.Should().Be(0);
        summary.CachedRequests.Should().Be(0);
        summary.RetryAttempts.Should().Be(0);
        summary.ServiceMetrics.Should().BeEmpty();
        summary.StatusCodeDistribution.Should().BeEmpty();
        summary.ErrorTypeDistribution.Should().BeEmpty();
    }

    #endregion

    #region DTO Computed Properties

    [Fact]
    public void ServiceCommunicationMetricsSummary_SuccessRate_CalculatesCorrectly()
    {
        var summary = new ServiceCommunicationMetricsSummary
        {
            TotalRequests = 100,
            SuccessfulRequests = 80
        };

        summary.SuccessRate.Should().Be(80);
    }

    [Fact]
    public void ServiceCommunicationMetricsSummary_CacheHitRate_CalculatesCorrectly()
    {
        var summary = new ServiceCommunicationMetricsSummary
        {
            TotalRequests = 100,
            CachedRequests = 25
        };

        summary.CacheHitRate.Should().Be(25);
    }

    [Fact]
    public void ServiceCommunicationMetricsSummary_ZeroRequests_RatesAreZero()
    {
        var summary = new ServiceCommunicationMetricsSummary();

        summary.SuccessRate.Should().Be(0);
        summary.CacheHitRate.Should().Be(0);
    }

    [Fact]
    public void ServiceMetrics_SuccessRate_CalculatesCorrectly()
    {
        var sm = new ServiceMetrics
        {
            TotalRequests = 200,
            SuccessfulRequests = 150
        };

        sm.SuccessRate.Should().Be(75);
    }

    [Fact]
    public void ServiceMetrics_ZeroRequests_SuccessRateIsZero()
    {
        var sm = new ServiceMetrics();
        sm.SuccessRate.Should().Be(0);
    }

    #endregion
}
