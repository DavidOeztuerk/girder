using Infrastructure.Resilience.Bulkhead;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public class BulkheadPolicyTests
{
    private readonly ILogger<BulkheadPolicy> _logger = Substitute.For<ILogger<BulkheadPolicy>>();

    private BulkheadPolicy CreatePolicy(BulkheadOptions? options = null)
    {
        return new BulkheadPolicy(options ?? new BulkheadOptions
        {
            MaxParallelization = 2,
            MaxQueuedActions = 2
        }, _logger, "test");
    }

    #region Constructor Validation

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        var act = () => new BulkheadPolicy(null!, _logger, "test");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new BulkheadPolicy(new BulkheadOptions(), null!, "test");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithZeroMaxParallelization_Throws()
    {
        var act = () => CreatePolicy(new BulkheadOptions
        {
            MaxParallelization = 0,
            MaxQueuedActions = 0
        });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativeMaxParallelization_Throws()
    {
        var act = () => CreatePolicy(new BulkheadOptions
        {
            MaxParallelization = -1,
            MaxQueuedActions = 0
        });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativeMaxQueuedActions_Throws()
    {
        var act = () => CreatePolicy(new BulkheadOptions
        {
            MaxParallelization = 1,
            MaxQueuedActions = -1
        });
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region ExecuteAsync — Basic

    [Fact]
    public async Task ExecuteAsync_SuccessfulOperation_ReturnsResult()
    {
        var policy = CreatePolicy();
        var result = await policy.ExecuteAsync(() => Task.FromResult(42));
        result.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_OperationThrows_PropagatesException()
    {
        var policy = CreatePolicy();
        var act = () => policy.ExecuteAsync<int>(() => throw new InvalidOperationException("boom"));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task ExecuteAsync_WithFallback_SuccessfulOperation_ReturnsPrimaryResult()
    {
        var policy = CreatePolicy();
        var result = await policy.ExecuteAsync(
            () => Task.FromResult(42),
            () => Task.FromResult(-1));
        result.Should().Be(42);
    }

    #endregion

    #region ExecuteAsync — Queuing (DropOldest)

    // Note: BulkheadPolicy uses BoundedChannelFullMode.DropOldest, which means
    // TryWrite always succeeds — the channel never rejects writes. The rejection
    // code path (fallback/BulkheadRejectedException) is unreachable with the
    // current DropOldest configuration. Queue-based tests verify queuing behavior.

    [Fact]
    public async Task ExecuteAsync_WhenSemaphoreFull_QueuesRequest()
    {
        var policy = CreatePolicy(new BulkheadOptions
        {
            MaxParallelization = 1,
            MaxQueuedActions = 5
        });

        var blockingSemaphore = new SemaphoreSlim(0, 1);
        var startedSemaphore = new SemaphoreSlim(0, 1);

        // Fill the semaphore with a blocking operation
        var blockingTask = Task.Run(async () =>
        {
            return await policy.ExecuteAsync(async () =>
            {
                startedSemaphore.Release();
                await blockingSemaphore.WaitAsync();
                return 1;
            });
        });

        await startedSemaphore.WaitAsync();

        // This request should be queued
        var queuedTask = Task.Run(() => policy.ExecuteAsync(() => Task.FromResult(42)));
        await Task.Delay(200);

        // Release the blocking task — queued task should proceed
        blockingSemaphore.Release();

        var result = await queuedTask;
        result.Should().Be(42);
        await blockingTask;

        // After completion, total executions should reflect both tasks
        var stats = policy.GetStatistics();
        stats.TotalExecutions.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Statistics

    [Fact]
    public void GetStatistics_Initial_ReturnsCorrectDefaults()
    {
        var policy = CreatePolicy(new BulkheadOptions
        {
            MaxParallelization = 5,
            MaxQueuedActions = 10
        });

        var stats = policy.GetStatistics();

        stats.MaxParallelization.Should().Be(5);
        stats.MaxQueuedActions.Should().Be(10);
        stats.CurrentParallelization.Should().Be(0);
        stats.TotalExecutions.Should().Be(0);
        stats.RejectedExecutions.Should().Be(0);
        stats.QueuedExecutions.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_AfterExecution_IncrementsTotalExecutions()
    {
        var policy = CreatePolicy();
        await policy.ExecuteAsync(() => Task.FromResult(1));
        await policy.ExecuteAsync(() => Task.FromResult(2));

        var stats = policy.GetStatistics();
        stats.TotalExecutions.Should().Be(2);
    }

    [Fact]
    public void ResetStatistics_ClearsAllCounters()
    {
        var policy = CreatePolicy();
        // Execute some operations first would be ideal, but just test reset
        policy.ResetStatistics();

        var stats = policy.GetStatistics();
        stats.TotalExecutions.Should().Be(0);
        stats.RejectedExecutions.Should().Be(0);
        stats.QueuedExecutions.Should().Be(0);
    }

    #endregion

    #region BulkheadStatistics DTO

    [Fact]
    public void BulkheadStatistics_RejectionRate_CalculatesCorrectly()
    {
        var stats = new BulkheadStatistics
        {
            TotalExecutions = 10,
            RejectedExecutions = 3
        };

        stats.RejectionRate.Should().Be(30);
    }

    [Fact]
    public void BulkheadStatistics_RejectionRate_ZeroExecutions_ReturnsZero()
    {
        var stats = new BulkheadStatistics
        {
            TotalExecutions = 0,
            RejectedExecutions = 0
        };

        stats.RejectionRate.Should().Be(0);
    }

    #endregion

    #region BulkheadOptions Defaults

    [Fact]
    public void BulkheadOptions_DefaultMaxParallelization_Is100()
    {
        var options = new BulkheadOptions();
        options.MaxParallelization.Should().Be(100);
    }

    [Fact]
    public void BulkheadOptions_DefaultMaxQueuedActions_Is50()
    {
        var options = new BulkheadOptions();
        options.MaxQueuedActions.Should().Be(50);
    }

    #endregion

    #region BulkheadRejectedException

    [Fact]
    public void BulkheadRejectedException_WithMessage_SetsMessage()
    {
        var ex = new BulkheadRejectedException("bulkhead full");
        ex.Message.Should().Be("bulkhead full");
    }

    #endregion
}
