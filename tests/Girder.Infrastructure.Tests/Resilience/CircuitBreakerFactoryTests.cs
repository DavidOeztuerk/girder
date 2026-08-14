using Girder.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public class CircuitBreakerFactoryTests
{
    private readonly ILoggerFactory _loggerFactory = Substitute.For<ILoggerFactory>();
    private readonly IOptionsMonitor<CircuitBreakerOptions> _optionsMonitor = Substitute.For<IOptionsMonitor<CircuitBreakerOptions>>();

    public CircuitBreakerFactoryTests()
    {
        _loggerFactory.CreateLogger<CircuitBreaker>()
            .Returns(Substitute.For<ILogger<CircuitBreaker>>());
        _optionsMonitor.Get(Arg.Any<string>())
            .Returns(new CircuitBreakerOptions
            {
                ExceptionsAllowedBeforeBreaking = 3,
                DurationOfBreak = TimeSpan.FromSeconds(30),
                Timeout = TimeSpan.FromSeconds(10)
            });
    }

    private CircuitBreakerFactory CreateFactory() =>
        new(_loggerFactory, _optionsMonitor);

    [Fact]
    public void GetCircuitBreaker_ByName_ReturnsInstance()
    {
        var factory = CreateFactory();
        var breaker = factory.GetCircuitBreaker("test");
        breaker.Should().NotBeNull();
    }

    [Fact]
    public void GetCircuitBreaker_SameName_ReturnsSameInstance()
    {
        var factory = CreateFactory();
        var b1 = factory.GetCircuitBreaker("test");
        var b2 = factory.GetCircuitBreaker("test");
        b1.Should().BeSameAs(b2);
    }

    [Fact]
    public void GetCircuitBreaker_DifferentNames_ReturnsDifferentInstances()
    {
        var factory = CreateFactory();
        var b1 = factory.GetCircuitBreaker("test1");
        var b2 = factory.GetCircuitBreaker("test2");
        b1.Should().NotBeSameAs(b2);
    }

    [Fact]
    public void GetCircuitBreaker_WithOptions_ReturnsInstance()
    {
        var factory = CreateFactory();
        var options = new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 5,
            DurationOfBreak = TimeSpan.FromSeconds(60),
            Timeout = TimeSpan.FromSeconds(15)
        };

        var breaker = factory.GetCircuitBreaker("custom", options);
        breaker.Should().NotBeNull();
    }

    [Fact]
    public void RemoveCircuitBreaker_RemovesInstance()
    {
        var factory = CreateFactory();
        var b1 = factory.GetCircuitBreaker("test");
        factory.RemoveCircuitBreaker("test");

        var b2 = factory.GetCircuitBreaker("test");
        b2.Should().NotBeSameAs(b1);
    }

    [Fact]
    public void RemoveCircuitBreaker_NonExistent_DoesNotThrow()
    {
        var factory = CreateFactory();
        var act = () => factory.RemoveCircuitBreaker("nonexistent");
        act.Should().NotThrow();
    }

    [Fact]
    public void GetCircuitBreakerNames_ReturnsAllNames()
    {
        var factory = CreateFactory();
        factory.GetCircuitBreaker("alpha");
        factory.GetCircuitBreaker("beta");

        var names = factory.GetCircuitBreakerNames().ToList();
        names.Should().Contain("alpha");
        names.Should().Contain("beta");
    }

    [Fact]
    public void GetCircuitBreakerNames_NoBreakers_ReturnsEmpty()
    {
        var factory = CreateFactory();
        factory.GetCircuitBreakerNames().Should().BeEmpty();
    }

    [Fact]
    public void GetAllStatistics_ReturnsStatsForAll()
    {
        var factory = CreateFactory();
        factory.GetCircuitBreaker("a");
        factory.GetCircuitBreaker("b");

        var stats = factory.GetAllStatistics();
        stats.Should().HaveCount(2);
        stats.Should().ContainKey("a");
        stats.Should().ContainKey("b");
    }

    [Fact]
    public void ResetAll_ResetsAllBreakers()
    {
        var factory = CreateFactory();
        var breaker = factory.GetCircuitBreaker("test");
        breaker.ForceOpen();
        breaker.State.Should().Be(CircuitBreakerState.Open);

        factory.ResetAll();

        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }
}
