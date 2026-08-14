using Girder.Infrastructure.Caching;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Models;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class CircuitBreakerRateLimitStoreTests
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
    public async Task IncrementAsync_WhenCircuitClosed_CallsPrimaryStore()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(1L);
        var store = CreateStore(inner: inner);

        var result = await store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        result.Should().Be(1L);
        await inner.Received(1).IncrementAsync("key", TimeSpan.FromMinutes(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncrementAsync_WhenCircuitBreakerDisabled_CallsPrimaryStore()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(5L);
        var store = CreateStore(inner: inner, options: new CircuitBreakerOptions { Enabled = false });

        var result = await store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        result.Should().Be(5L);
    }

    [Fact]
    public async Task GetCountAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.GetCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(10L);
        var store = CreateStore(inner: inner);

        var result = await store.GetCountAsync("key");

        result.Should().Be(10L);
    }

    [Fact]
    public async Task ExistsAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.ExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var store = CreateStore(inner: inner);

        var result = await store.ExistsAsync("key");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        var store = CreateStore(inner: inner);

        var result = await store.DeleteAsync("key");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExpireAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.ExpireAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        var store = CreateStore(inner: inner);

        var result = await store.ExpireAsync("key", TimeSpan.FromMinutes(1));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetTimeToLiveAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.GetTimeToLiveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(TimeSpan.FromMinutes(5));
        var store = CreateStore(inner: inner);

        var result = await store.GetTimeToLiveAsync("key");

        result.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ExecuteScriptAsync_WhenPrimarySucceeds_ReturnsPrimaryResult()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.ExecuteScriptAsync(Arg.Any<string>(), Arg.Any<string[]>(), Arg.Any<object[]>(), Arg.Any<CancellationToken>())
            .Returns(42L);
        var store = CreateStore(inner: inner);

        var result = await store.ExecuteScriptAsync("script", Array.Empty<string>(), Array.Empty<object>());

        result.Should().Be(42L);
    }

    [Fact]
    public async Task IncrementAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsDefault()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        result.Should().Be(0L);
    }

    [Fact]
    public async Task IncrementAsync_WhenPrimaryFails_WithUseInMemoryFallback_UsesFallback()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var memCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var fallback = new InMemoryRateLimitStore(memCache, Substitute.For<ILogger<InMemoryRateLimitStore>>());

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.UseInMemory
        };
        var store = CreateStore(inner: inner, fallback: fallback, options: options);

        var result = await store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        result.Should().BeGreaterThan(0L);
    }

    [Fact]
    public async Task IncrementAsync_AfterThresholdFailures_OpensCircuit()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 2,
            FallbackBehavior = CircuitBreakerFallback.AllowAll,
            OpenTimeout = TimeSpan.FromHours(1) // Keep open for a long time
        };
        var store = CreateStore(inner: inner, options: options);

        // Trigger failures to open circuit
        await store.IncrementAsync("key", TimeSpan.FromMinutes(1));
        await store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        // Now circuit is open - should use fallback directly
        var result = await store.IncrementAsync("key", TimeSpan.FromMinutes(1));

        result.Should().Be(0L); // AllowAll default
    }

    [Fact]
    public async Task GetCountAsync_WhenPrimaryFails_WithAllowAllFallback_ReturnsZero()
    {
        var inner = Substitute.For<IDistributedRateLimitStore>();
        inner.GetCountAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var options = new CircuitBreakerOptions
        {
            Enabled = true,
            FailureThreshold = 3,
            FallbackBehavior = CircuitBreakerFallback.AllowAll
        };
        var store = CreateStore(inner: inner, options: options);

        var result = await store.GetCountAsync("key");

        result.Should().Be(0L);
    }
}
