using Infrastructure.Caching;
using Infrastructure.Extensions;
using Infrastructure.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class CircuitBreakerRateLimitStoreTopUpTests
{
    private static CircuitBreakerRateLimitStore CreateStore(
        IDistributedRateLimitStore? inner = null,
        InMemoryRateLimitStore? fallback = null,
        CircuitBreakerOptions? options = null)
    {
        inner ??= Substitute.For<IDistributedRateLimitStore>();
        fallback ??= new InMemoryRateLimitStore(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            Substitute.For<ILogger<InMemoryRateLimitStore>>());
        options ??= new CircuitBreakerOptions { Enabled = true, FailureThreshold = 3 };
        var logger = Substitute.For<ILogger<CircuitBreakerRateLimitStore>>();

        return new CircuitBreakerRateLimitStore(inner, fallback, logger, options);
    }

    [Fact]
    public async Task ExpireAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsTrue()
    {
        // AllowAll fallback: HandleFailure calls GetDefaultAllowResult<bool>() which returns true
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.ExpireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.ExpireAsync("key", TimeSpan.FromMinutes(1));

        // AllowAll: GetDefaultAllowResult<bool>() returns true (allow the operation)
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsTrue()
    {
        // AllowAll fallback: HandleFailure calls GetDefaultAllowResult<bool>() which returns true
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.DeleteAsync("key");

        // AllowAll: GetDefaultAllowResult<bool>() returns true (allow the operation)
        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetTimeToLiveAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsNull()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.GetTimeToLiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.GetTimeToLiveAsync("key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExistsAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsTrue()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.ExistsAsync("key");

        // AllowAll means we let it through → true
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteScriptAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsZero()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.ExecuteScriptAsync("script", Array.Empty<string>(), Array.Empty<object>());

        result.Should().Be(0L);
    }

    [Fact]
    public async Task IncrementAsync_WhenCircuitDisabled_PrimaryFails_Throws()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions { Enabled = false };
        var store = CreateStore(inner: inner, options: options);

        var act = () => store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        await act.Should().ThrowAsync<Exception>();
    }
}
