using Infrastructure.Resilience;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public class CircuitBreakerTests
{
    private readonly ILogger<CircuitBreaker> _logger = Substitute.For<ILogger<CircuitBreaker>>();

    private CircuitBreaker CreateBreaker(CircuitBreakerOptions? options = null)
    {
        return new CircuitBreaker(options ?? new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 10
        }, _logger, "test");
    }

    [Fact]
    public void InitialState_ShouldBeClosed()
    {
        var breaker = CreateBreaker();
        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task SuccessfulExecution_ShouldReturnResult()
    {
        var breaker = CreateBreaker();
        var result = await breaker.ExecuteAsync(() => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task SuccessfulExecution_ShouldRemainClosed()
    {
        var breaker = CreateBreaker();
        await breaker.ExecuteAsync(() => Task.FromResult(42));
        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task ConsecutiveFailures_ShouldTripCircuit()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100
        });

        for (int i = 0; i < 3; i++)
        {
            try
            {
                await breaker.ExecuteAsync<int>(
                    () => throw new InvalidOperationException("fail"),
                    () => Task.FromResult(-1));
            }
            catch { }
        }

        breaker.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task OpenCircuit_ShouldExecuteFallback()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 1,
            DurationOfBreak = TimeSpan.FromMinutes(5),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100
        });

        // Trip the circuit
        try
        {
            await breaker.ExecuteAsync<int>(
                () => throw new InvalidOperationException("fail"),
                () => Task.FromResult(-1));
        }
        catch { }

        // Next call should use fallback directly (circuit is open)
        var result = await breaker.ExecuteAsync(
            () => Task.FromResult(42),
            () => Task.FromResult(-1));

        result.Should().Be(-1);
    }

    [Fact]
    public async Task OpenCircuit_WithoutFallback_ShouldThrowCircuitBreakerOpenException()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 1,
            DurationOfBreak = TimeSpan.FromMinutes(5),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100
        });

        // Trip the circuit
        try
        {
            await breaker.ExecuteAsync<int>(
                () => throw new InvalidOperationException("fail"),
                () => Task.FromResult(-1));
        }
        catch { }

        // Next call without fallback should throw
        var act = () => breaker.ExecuteAsync(() => Task.FromResult(42));
        await act.Should().ThrowAsync<CircuitBreakerOpenException>();
    }

    [Fact]
    public async Task SuccessAfterFailure_ShouldResetConsecutiveFailures()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100
        });

        // 2 failures
        for (int i = 0; i < 2; i++)
        {
            try { await breaker.ExecuteAsync<int>(() => throw new Exception("fail")); }
            catch { }
        }

        // 1 success resets consecutive failures
        await breaker.ExecuteAsync(() => Task.FromResult(1));

        // 2 more failures should NOT trip circuit (only 2 consecutive, need 3)
        for (int i = 0; i < 2; i++)
        {
            try { await breaker.ExecuteAsync<int>(() => throw new Exception("fail")); }
            catch { }
        }

        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public void Reset_ShouldCloseCircuit()
    {
        var breaker = CreateBreaker();
        breaker.ForceOpen();
        breaker.State.Should().Be(CircuitBreakerState.Open);

        breaker.Reset();
        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public void ForceOpen_ShouldOpenCircuit()
    {
        var breaker = CreateBreaker();
        breaker.ForceOpen();
        breaker.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public void ForceOpen_ShouldIncrementTripCount()
    {
        var breaker = CreateBreaker();
        breaker.ForceOpen();
        var stats = breaker.GetStatistics();
        stats.TripCount.Should().Be(1);
    }

    [Fact]
    public async Task Statistics_ShouldTrackSuccessAndFailure()
    {
        var breaker = CreateBreaker();

        await breaker.ExecuteAsync(() => Task.FromResult(1));
        await breaker.ExecuteAsync(() => Task.FromResult(2));

        try { await breaker.ExecuteAsync<int>(() => throw new Exception("fail")); }
        catch { }

        var stats = breaker.GetStatistics();
        stats.SuccessCount.Should().Be(2);
        stats.FailureCount.Should().Be(1);
        stats.ConsecutiveFailures.Should().Be(1);
        stats.LastError.Should().Be("fail");
    }

    [Fact]
    public void Constructor_WithInvalidOptions_ShouldThrow()
    {
        var act = () => CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 0,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10)
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ExceptionsAllowedBeforeBreaking*");
    }

    [Fact]
    public void Constructor_WithZeroDuration_ShouldThrow()
    {
        var act = () => CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.Zero,
            Timeout = TimeSpan.FromSeconds(10)
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*DurationOfBreak*");
    }

    [Fact]
    public void Constructor_WithZeroTimeout_ShouldThrow()
    {
        var act = () => CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.Zero
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Timeout*");
    }

    [Fact]
    public void Constructor_WithInvalidFailureThreshold_ShouldThrow()
    {
        var act = () => CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 1.5
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*FailureThreshold*");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrow()
    {
        var act = () => new CircuitBreaker(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        var act = () => new CircuitBreaker(new CircuitBreakerOptions(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task VoidExecution_ShouldWork()
    {
        var breaker = CreateBreaker();
        var executed = false;

        await breaker.ExecuteAsync(() =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        executed.Should().BeTrue();
    }

    [Fact]
    public async Task VoidExecution_WithFallback_ShouldWork()
    {
        var breaker = CreateBreaker();
        breaker.ForceOpen();

        var fallbackExecuted = false;
        await breaker.ExecuteAsync(
            () => Task.CompletedTask,
            () =>
            {
                fallbackExecuted = true;
                return Task.CompletedTask;
            });

        fallbackExecuted.Should().BeTrue();
    }

    #region HalfOpen Transition

    [Fact]
    public async Task HalfOpen_SuccessfulExecution_ShouldCloseCircuit()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 1,
            DurationOfBreak = TimeSpan.FromMilliseconds(1),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100
        });

        // Trip the circuit
        try
        {
            await breaker.ExecuteAsync<int>(
                () => throw new InvalidOperationException("fail"),
                () => Task.FromResult(-1));
        }
        catch { }

        breaker.State.Should().Be(CircuitBreakerState.Open);

        // Wait for DurationOfBreak to elapse
        await Task.Delay(50);

        // Next call should transition to HalfOpen and succeed → Closed
        var result = await breaker.ExecuteAsync(() => Task.FromResult(99));
        result.Should().Be(99);
        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    [Fact]
    public async Task HalfOpen_FailedExecution_ShouldReopenCircuit()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 1,
            DurationOfBreak = TimeSpan.FromMilliseconds(1),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100
        });

        // Trip the circuit
        try
        {
            await breaker.ExecuteAsync<int>(
                () => throw new InvalidOperationException("fail"),
                () => Task.FromResult(-1));
        }
        catch { }

        breaker.State.Should().Be(CircuitBreakerState.Open);
        await Task.Delay(50);

        // Fail again in half-open → circuit should reopen
        try
        {
            await breaker.ExecuteAsync<int>(
                () => throw new InvalidOperationException("fail again"),
                () => Task.FromResult(-1));
        }
        catch { }

        breaker.State.Should().Be(CircuitBreakerState.Open);
    }

    #endregion

    #region Failure Rate Threshold

    [Fact]
    public async Task FailureRateThreshold_ShouldTripCircuit()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 100, // high, so consecutive failures won't trip
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 10
        });

        // 5 successes + 5 failures = 50% failure rate at threshold
        for (int i = 0; i < 5; i++)
            await breaker.ExecuteAsync(() => Task.FromResult(1));

        for (int i = 0; i < 5; i++)
        {
            try { await breaker.ExecuteAsync<int>(() => throw new Exception("fail")); }
            catch { }
        }

        // 10 requests, 50% failure rate meets threshold → should trip
        breaker.State.Should().Be(CircuitBreakerState.Open);
    }

    [Fact]
    public async Task FailureRate_BelowMinimumThroughput_ShouldNotTrip()
    {
        var breaker = CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 100,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = 0.5,
            MinimumThroughput = 100 // high minimum
        });

        // Only 4 requests (below throughput), all failing
        for (int i = 0; i < 4; i++)
        {
            try { await breaker.ExecuteAsync<int>(() => throw new Exception("fail")); }
            catch { }
        }

        // Below MinimumThroughput and below consecutive threshold → stays closed
        breaker.State.Should().Be(CircuitBreakerState.Closed);
    }

    #endregion

    #region Statistics Computed Properties

    [Fact]
    public void Statistics_InitialState_AllZeros()
    {
        var breaker = CreateBreaker();
        var stats = breaker.GetStatistics();

        stats.SuccessCount.Should().Be(0);
        stats.FailureCount.Should().Be(0);
        stats.TotalCount.Should().Be(0);
        stats.TripCount.Should().Be(0);
        stats.ConsecutiveFailures.Should().Be(0);
        stats.AverageResponseTime.Should().Be(0);
        stats.LastError.Should().BeNull();
        stats.LastOpenedAt.Should().BeNull();
    }

    [Fact]
    public async Task Statistics_AverageResponseTime_IsTracked()
    {
        var breaker = CreateBreaker();
        await breaker.ExecuteAsync(() => Task.FromResult(1));

        var stats = breaker.GetStatistics();
        stats.AverageResponseTime.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void CircuitBreakerStatistics_SuccessRate_CalculatesCorrectly()
    {
        var stats = new CircuitBreakerStatistics
        {
            SuccessCount = 7,
            FailureCount = 3
        };

        stats.TotalCount.Should().Be(10);
        stats.SuccessRate.Should().Be(70);
        stats.FailureRate.Should().Be(30);
    }

    [Fact]
    public void CircuitBreakerStatistics_NoRequests_RatesAreZero()
    {
        var stats = new CircuitBreakerStatistics();

        stats.TotalCount.Should().Be(0);
        stats.SuccessRate.Should().Be(0);
        stats.FailureRate.Should().Be(0);
    }

    [Fact]
    public void ForceOpen_MultipleTimes_IncrementsTripCount()
    {
        var breaker = CreateBreaker();
        breaker.ForceOpen();
        breaker.Reset();
        breaker.ForceOpen();

        var stats = breaker.GetStatistics();
        stats.TripCount.Should().Be(2);
    }

    [Fact]
    public void ForceOpen_SetsLastOpenedAt()
    {
        var before = DateTime.UtcNow;
        var breaker = CreateBreaker();
        breaker.ForceOpen();
        var after = DateTime.UtcNow;

        var stats = breaker.GetStatistics();
        stats.LastOpenedAt.Should().NotBeNull();
        stats.LastOpenedAt!.Value.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    #endregion

    #region CircuitBreakerOpenException

    [Fact]
    public void CircuitBreakerOpenException_WithMessage_SetsMessage()
    {
        var ex = new CircuitBreakerOpenException("circuit is open");
        ex.Message.Should().Be("circuit is open");
    }

    [Fact]
    public void CircuitBreakerOpenException_WithInnerException_SetsInner()
    {
        var inner = new InvalidOperationException("root cause");
        var ex = new CircuitBreakerOpenException("circuit is open", inner);
        ex.InnerException.Should().BeSameAs(inner);
    }

    #endregion

    #region Constructor with negative FailureThreshold

    [Fact]
    public void Constructor_WithNegativeFailureThreshold_ShouldThrow()
    {
        var act = () => CreateBreaker(new CircuitBreakerOptions
        {
            ExceptionsAllowedBeforeBreaking = 3,
            DurationOfBreak = TimeSpan.FromSeconds(30),
            Timeout = TimeSpan.FromSeconds(10),
            FailureThreshold = -0.1
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*FailureThreshold*");
    }

    #endregion
}
