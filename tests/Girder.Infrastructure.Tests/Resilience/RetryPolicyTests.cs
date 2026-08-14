using Girder.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace Girder.Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public class RetryPolicyTests
{
    private readonly ILogger<RetryPolicy> _logger = Substitute.For<ILogger<RetryPolicy>>();

    private RetryPolicy CreatePolicy(RetryPolicyOptions? options = null)
    {
        return new RetryPolicy(options ?? new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromMilliseconds(500),
            BackoffStrategy = BackoffStrategy.Fixed,
            ShouldRetry = _ => true
        }, _logger, "test");
    }

    [Fact]
    public async Task SuccessfulExecution_ShouldReturnResult()
    {
        var policy = CreatePolicy();
        var result = await policy.ExecuteAsync(() => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task TransientFailure_ShouldRetryAndSucceed()
    {
        var policy = CreatePolicy();
        var attempt = 0;

        var result = await policy.ExecuteAsync(() =>
        {
            attempt++;
            if (attempt < 3) throw new HttpRequestException("transient");
            return Task.FromResult(42);
        });

        result.Should().Be(42);
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task AllRetriesExhausted_ShouldThrowRetryPolicyException()
    {
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 2,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            BackoffStrategy = BackoffStrategy.Fixed,
            ShouldRetry = _ => true
        });

        var act = () => policy.ExecuteAsync<int>(() => throw new HttpRequestException("fail"));
        await act.Should().ThrowAsync<RetryPolicyException>()
            .Where(ex => ex.Attempts.Count == 3); // initial + 2 retries
    }

    [Fact]
    public async Task NonRetryableException_ShouldNotRetry()
    {
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            BackoffStrategy = BackoffStrategy.Fixed,
            ShouldRetry = ex => ex is HttpRequestException
        });

        var attempt = 0;
        var act = () => policy.ExecuteAsync<int>(() =>
        {
            attempt++;
            throw new ArgumentException("not retryable");
        });

        await act.Should().ThrowAsync<RetryPolicyException>();
        attempt.Should().Be(1); // no retries
    }

    [Fact]
    public async Task Statistics_ShouldTrackExecutions()
    {
        var policy = CreatePolicy();

        await policy.ExecuteAsync(() => Task.FromResult(1));
        await policy.ExecuteAsync(() => Task.FromResult(2));

        var stats = policy.GetStatistics();
        stats.TotalExecutions.Should().Be(2);
        stats.SuccessfulExecutions.Should().Be(2);
        stats.FailedExecutions.Should().Be(0);
    }

    [Fact]
    public async Task Statistics_ShouldTrackRetryAttempts()
    {
        var policy = CreatePolicy();
        var attempt = 0;

        await policy.ExecuteAsync(() =>
        {
            attempt++;
            if (attempt == 1) throw new HttpRequestException("transient");
            return Task.FromResult(1);
        });

        var stats = policy.GetStatistics();
        stats.RetryAttempts.Should().Be(1);
        stats.SuccessfulExecutions.Should().Be(1);
    }

    [Fact]
    public void ResetStatistics_ShouldClearAll()
    {
        var policy = CreatePolicy();
        policy.ResetStatistics();

        var stats = policy.GetStatistics();
        stats.TotalExecutions.Should().Be(0);
        stats.SuccessfulExecutions.Should().Be(0);
        stats.FailedExecutions.Should().Be(0);
        stats.RetryAttempts.Should().Be(0);
    }

    [Fact]
    public async Task VoidExecution_ShouldWork()
    {
        var policy = CreatePolicy();
        var executed = false;

        await policy.ExecuteAsync(() =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        executed.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNegativeMaxRetries_ShouldThrow()
    {
        var act = () => CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = -1,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromMilliseconds(100)
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxRetryAttempts*");
    }

    [Fact]
    public void Constructor_WithZeroBaseDelay_ShouldThrow()
    {
        var act = () => CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.FromMilliseconds(100)
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*BaseDelay*");
    }

    [Fact]
    public void Constructor_WithMaxDelayLessThanBase_ShouldThrow()
    {
        var act = () => CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromMilliseconds(10)
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxDelay*");
    }

    [Fact]
    public void Constructor_WithNullOptions_ShouldThrow()
    {
        var act = () => new RetryPolicy(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        var act = () => new RetryPolicy(new RetryPolicyOptions(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultShouldRetryPredicate_ShouldRetryHttpRequestException()
    {
        RetryPolicyOptions.DefaultShouldRetryPredicate(new HttpRequestException())
            .Should().BeTrue();
    }

    [Fact]
    public void DefaultShouldRetryPredicate_ShouldRetryTimeoutException()
    {
        RetryPolicyOptions.DefaultShouldRetryPredicate(new TimeoutException())
            .Should().BeTrue();
    }

    [Fact]
    public void DefaultShouldRetryPredicate_ShouldNotRetryArgumentException()
    {
        RetryPolicyOptions.DefaultShouldRetryPredicate(new ArgumentException())
            .Should().BeFalse();
    }

    [Fact]
    public async Task OnRetryCallback_ShouldBeInvoked()
    {
        var policy = CreatePolicy();
        var retryCallbacks = new List<int>();
        var attempt = 0;

        await policy.ExecuteAsync(
            () =>
            {
                attempt++;
                if (attempt == 1) throw new HttpRequestException("transient");
                return Task.FromResult(1);
            },
            (retryNumber, _, _) => retryCallbacks.Add(retryNumber));

        retryCallbacks.Should().ContainSingle().Which.Should().Be(1);
    }

    [Fact]
    public async Task Statistics_ShouldTrackExceptionTypes()
    {
        var policy = CreatePolicy();

        try
        {
            await policy.ExecuteAsync<int>(() => throw new HttpRequestException("fail"));
        }
        catch { }

        var stats = policy.GetStatistics();
        stats.ExceptionTypes.Should().ContainKey("HttpRequestException");
    }

    #region Backoff Strategies

    [Fact]
    public async Task FixedBackoff_AllRetriesUseSameDelay()
    {
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromSeconds(1),
            BackoffStrategy = BackoffStrategy.Fixed,
            ShouldRetry = _ => true
        });

        try
        {
            await policy.ExecuteAsync<int>(
                () => throw new HttpRequestException("fail"),
                (_, _, delay) => delays.Add(delay));
        }
        catch { }

        delays.Should().HaveCount(3);
        delays.Should().AllBeEquivalentTo(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public async Task LinearBackoff_DelayIncreasesLinearly()
    {
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromSeconds(1),
            BackoffStrategy = BackoffStrategy.Linear,
            ShouldRetry = _ => true
        });

        try
        {
            await policy.ExecuteAsync<int>(
                () => throw new HttpRequestException("fail"),
                (_, _, delay) => delays.Add(delay));
        }
        catch { }

        // Linear: base*(attempt+1) → 10ms, 20ms, 30ms
        delays.Should().HaveCount(3);
        delays[0].TotalMilliseconds.Should().Be(10);
        delays[1].TotalMilliseconds.Should().Be(20);
        delays[2].TotalMilliseconds.Should().Be(30);
    }

    [Fact]
    public async Task ExponentialBackoff_DelayDoublesEachAttempt()
    {
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromSeconds(1),
            BackoffStrategy = BackoffStrategy.Exponential,
            ShouldRetry = _ => true
        });

        try
        {
            await policy.ExecuteAsync<int>(
                () => throw new HttpRequestException("fail"),
                (_, _, delay) => delays.Add(delay));
        }
        catch { }

        // Exponential: base*2^attempt → 10ms, 20ms, 40ms
        delays.Should().HaveCount(3);
        delays[0].TotalMilliseconds.Should().Be(10);
        delays[1].TotalMilliseconds.Should().Be(20);
        delays[2].TotalMilliseconds.Should().Be(40);
    }

    [Fact]
    public async Task ExponentialWithJitter_DelayIsWithin25PercentOfExponential()
    {
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(10),
            BackoffStrategy = BackoffStrategy.ExponentialWithJitter,
            ShouldRetry = _ => true
        });

        try
        {
            await policy.ExecuteAsync<int>(
                () => throw new HttpRequestException("fail"),
                (_, _, delay) => delays.Add(delay));
        }
        catch { }

        delays.Should().HaveCount(3);

        // Attempt 0: base=100ms, jitter ±25% → 75-125ms
        delays[0].TotalMilliseconds.Should().BeInRange(75, 125);
        // Attempt 1: base=200ms, jitter ±25% → 150-250ms
        delays[1].TotalMilliseconds.Should().BeInRange(150, 250);
        // Attempt 2: base=400ms, jitter ±25% → 300-500ms
        delays[2].TotalMilliseconds.Should().BeInRange(300, 500);
    }

    [Fact]
    public async Task MaxDelayCap_ShouldLimitDelay()
    {
        var delays = new List<TimeSpan>();
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromMilliseconds(150),
            BackoffStrategy = BackoffStrategy.Exponential,
            ShouldRetry = _ => true
        });

        try
        {
            await policy.ExecuteAsync<int>(
                () => throw new HttpRequestException("fail"),
                (_, _, delay) => delays.Add(delay));
        }
        catch { }

        // Exponential: 100, 200, 400 → capped to 100, 150, 150
        delays[0].TotalMilliseconds.Should().Be(100);
        delays[1].TotalMilliseconds.Should().Be(150);
        delays[2].TotalMilliseconds.Should().Be(150);
    }

    #endregion

    #region Zero Max Retries

    [Fact]
    public async Task ZeroMaxRetries_ShouldNotRetry()
    {
        var attempts = 0;
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 0,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            BackoffStrategy = BackoffStrategy.Fixed,
            ShouldRetry = _ => true
        });

        var act = () => policy.ExecuteAsync<int>(() =>
        {
            attempts++;
            throw new HttpRequestException("fail");
        });

        await act.Should().ThrowAsync<RetryPolicyException>();
        attempts.Should().Be(1);
    }

    #endregion

    #region DefaultShouldRetryPredicate — Additional Types

    [Fact]
    public void DefaultShouldRetryPredicate_ShouldRetrySocketException()
    {
        RetryPolicyOptions.DefaultShouldRetryPredicate(new SocketException())
            .Should().BeTrue();
    }

    [Fact]
    public void DefaultShouldRetryPredicate_ShouldRetryTaskCanceledException()
    {
        RetryPolicyOptions.DefaultShouldRetryPredicate(new TaskCanceledException())
            .Should().BeTrue();
    }

    [Fact]
    public void DefaultShouldRetryPredicate_ShouldNotRetryInvalidOperationException()
    {
        RetryPolicyOptions.DefaultShouldRetryPredicate(new InvalidOperationException())
            .Should().BeFalse();
    }

    #endregion

    #region Statistics Computed Properties

    [Fact]
    public void RetryPolicyStatistics_SuccessRate_CalculatesCorrectly()
    {
        var stats = new RetryPolicyStatistics
        {
            TotalExecutions = 10,
            SuccessfulExecutions = 7,
            FailedExecutions = 3
        };

        stats.SuccessRate.Should().Be(70);
    }

    [Fact]
    public void RetryPolicyStatistics_NoExecutions_RateIsZero()
    {
        var stats = new RetryPolicyStatistics();
        stats.SuccessRate.Should().Be(0);
        stats.AverageRetryAttempts.Should().Be(0);
    }

    [Fact]
    public void RetryPolicyStatistics_AverageRetryAttempts_CalculatesCorrectly()
    {
        var stats = new RetryPolicyStatistics
        {
            TotalExecutions = 4,
            RetryAttempts = 6
        };

        stats.AverageRetryAttempts.Should().Be(1.5);
    }

    #endregion

    #region RetryDistribution

    [Fact]
    public async Task Statistics_RetryDistribution_TracksCorrectly()
    {
        var policy = CreatePolicy();

        // 1 success with no retries
        await policy.ExecuteAsync(() => Task.FromResult(1));

        // 1 success after 1 retry
        var attempt = 0;
        await policy.ExecuteAsync(() =>
        {
            attempt++;
            if (attempt == 1) throw new HttpRequestException("transient");
            return Task.FromResult(1);
        });

        var stats = policy.GetStatistics();
        stats.RetryDistribution.Should().ContainKey(0); // no-retry success
        stats.RetryDistribution[0].Should().Be(1);
        stats.RetryDistribution.Should().ContainKey(1); // 1-retry success
        stats.RetryDistribution[1].Should().Be(1);
    }

    #endregion

    #region RetryPolicyException

    [Fact]
    public async Task RetryPolicyException_ContainsAllAttempts()
    {
        var policy = CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 2,
            BaseDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(10),
            BackoffStrategy = BackoffStrategy.Fixed,
            ShouldRetry = _ => true
        });

        RetryPolicyException? caught = null;
        try
        {
            await policy.ExecuteAsync<int>(() => throw new HttpRequestException("boom"));
        }
        catch (RetryPolicyException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught!.Attempts.Should().HaveCount(3); // initial + 2 retries
        caught.InnerException.Should().BeOfType<HttpRequestException>();
        caught.Message.Should().Contain("3 attempts");
    }

    #endregion

    #region ResetStatistics after executions

    [Fact]
    public async Task ResetStatistics_AfterExecutions_ClearsEverything()
    {
        var policy = CreatePolicy();
        await policy.ExecuteAsync(() => Task.FromResult(1));

        try
        {
            await policy.ExecuteAsync<int>(() => throw new HttpRequestException("fail"));
        }
        catch { }

        policy.ResetStatistics();

        var stats = policy.GetStatistics();
        stats.TotalExecutions.Should().Be(0);
        stats.SuccessfulExecutions.Should().Be(0);
        stats.FailedExecutions.Should().Be(0);
        stats.RetryAttempts.Should().Be(0);
        stats.RetryDistribution.Should().BeEmpty();
        stats.ExceptionTypes.Should().BeEmpty();
    }

    #endregion

    #region Constructor — ZeroMaxDelay

    [Fact]
    public void Constructor_WithZeroMaxDelay_ShouldThrow()
    {
        var act = () => CreatePolicy(new RetryPolicyOptions
        {
            MaxRetryAttempts = 3,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.Zero
        });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*MaxDelay*");
    }

    #endregion
}
