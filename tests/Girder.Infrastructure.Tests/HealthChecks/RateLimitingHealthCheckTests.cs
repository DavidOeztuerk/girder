using Girder.Abstractions.Caching;
using Girder.Infrastructure.Caching;
using Girder.Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class RateLimitingHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("rate-limiting", check, null, null)
        };

    private static IDistributedRateLimitStore CreateHealthyStore()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();

        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = true, CurrentCount = 1, Limit = 10 });

        store.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(1L);

        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        store.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        return store;
    }

    [Fact]
    public async Task CheckHealthAsync_WhenAllOperationsSucceed_ReturnsHealthy()
    {
        var store = CreateHealthyStore();
        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("functioning correctly");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenSlidingWindowNotAllowed_ReturnsDegraded()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();
        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = false, CurrentCount = 1, Limit = 10 });
        store.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenIncrementReturnsUnexpectedValue_ReturnsDegraded()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();
        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = true, CurrentCount = 1, Limit = 10 });
        // IncrementAsync returns 2 instead of 1
        store.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(2L);
        store.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenKeyDoesNotExistAfterIncrement_ReturnsDegraded()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();
        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = true, CurrentCount = 1, Limit = 10 });
        store.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        store.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        store.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_ReturnsUnhealthy()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();
        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Store failure"));

        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenRedisConnectionError_ReturnsDegraded()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();
        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis connection refused"));

        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenTaskCanceled_ReturnsUnhealthy()
    {
        var store = Substitute.For<IDistributedRateLimitStore>();
        store.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Timeout"));

        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("timed out");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithResponseTime()
    {
        var store = CreateHealthyStore();
        var logger = Substitute.For<ILogger<RateLimitingHealthCheck>>();
        var check = new RateLimitingHealthCheck(store, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("response_time_ms");
        result.Data.Should().ContainKey("store_type");
    }
}
