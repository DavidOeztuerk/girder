using Infrastructure.HealthChecks;
using Infrastructure.Resilience;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class CircuitBreakerHealthCheckTests
{
    private readonly ICircuitBreakerFactory _factory = Substitute.For<ICircuitBreakerFactory>();
    private readonly ILogger<CircuitBreakerHealthCheck> _logger = Substitute.For<ILogger<CircuitBreakerHealthCheck>>();

    private CircuitBreakerHealthCheck CreateCheck() => new(_factory, _logger);

    [Fact]
    public async Task CheckHealthAsync_NoCircuitBreakers_ReturnsHealthy()
    {
        _factory.GetAllStatistics().Returns(new Dictionary<string, CircuitBreakerStatistics>());

        var check = CreateCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("CircuitBreaker", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("healthy");
    }

    [Fact]
    public async Task CheckHealthAsync_AllClosed_ReturnsHealthy()
    {
        _factory.GetAllStatistics().Returns(new Dictionary<string, CircuitBreakerStatistics>
        {
            ["svcA"] = new CircuitBreakerStatistics
            {
                State = CircuitBreakerState.Closed,
                SuccessCount = 100,
                FailureCount = 2
            }
        });

        var check = CreateCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("CircuitBreaker", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task CheckHealthAsync_OpenCircuitBreaker_ReturnsDegraded()
    {
        _factory.GetAllStatistics().Returns(new Dictionary<string, CircuitBreakerStatistics>
        {
            ["svcA"] = new CircuitBreakerStatistics
            {
                State = CircuitBreakerState.Open,
                SuccessCount = 10,
                FailureCount = 5
            }
        });

        var check = CreateCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("CircuitBreaker", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("OPEN");
    }

    [Fact]
    public async Task CheckHealthAsync_HighFailureRate_ReturnsDegraded()
    {
        _factory.GetAllStatistics().Returns(new Dictionary<string, CircuitBreakerStatistics>
        {
            ["svcA"] = new CircuitBreakerStatistics
            {
                State = CircuitBreakerState.Closed,
                SuccessCount = 4,
                FailureCount = 7  // >50% failure rate, TotalCount=11 > 10
            }
        });

        var check = CreateCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("CircuitBreaker", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("failure rate");
    }

    [Fact]
    public async Task CheckHealthAsync_DataContainsCircuitBreakerInfo()
    {
        _factory.GetAllStatistics().Returns(new Dictionary<string, CircuitBreakerStatistics>
        {
            ["svcA"] = new CircuitBreakerStatistics
            {
                State = CircuitBreakerState.Closed,
                SuccessCount = 10,
                FailureCount = 0
            }
        });

        var check = CreateCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("CircuitBreaker", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("circuit_breakers");
        result.Data.Should().ContainKey("total_circuit_breakers");
        result.Data["total_circuit_breakers"].Should().Be(1);
    }

    [Fact]
    public async Task CheckHealthAsync_FactoryThrows_ReturnsDegraded()
    {
        _factory.GetAllStatistics().Throws(new InvalidOperationException("factory error"));

        var check = CreateCheck();
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("CircuitBreaker", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("failed");
    }
}
