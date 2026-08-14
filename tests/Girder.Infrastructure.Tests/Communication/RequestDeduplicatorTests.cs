using Girder.Infrastructure.Communication.Deduplication;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class RequestDeduplicatorTests
{
    private static RequestDeduplicator CreateDeduplicator()
    {
        var logger = Substitute.For<ILogger<RequestDeduplicator>>();
        return new RequestDeduplicator(logger);
    }

    #region GetStatistics — Initial State

    [Fact]
    public void GetStatistics_InitialState_AllZeros()
    {
        var dedup = CreateDeduplicator();

        var stats = dedup.GetStatistics();

        stats.TotalRequests.Should().Be(0);
        stats.DeduplicatedRequests.Should().Be(0);
        stats.UniqueRequests.Should().Be(0);
        stats.CurrentInflightRequests.Should().Be(0);
    }

    #endregion

    #region GetStatistics — After ExecuteAsync

    [Fact]
    public async Task GetStatistics_AfterOneRequest_ShowsOneTotal()
    {
        var dedup = CreateDeduplicator();

        await dedup.ExecuteAsync<string>("key-1", () => Task.FromResult<string?>("result"));

        var stats = dedup.GetStatistics();
        stats.TotalRequests.Should().Be(1);
        stats.UniqueRequests.Should().Be(1);
        stats.DeduplicatedRequests.Should().Be(0);
        stats.CurrentInflightRequests.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_AfterMultipleDistinctKeys_CountsAllAsUnique()
    {
        var dedup = CreateDeduplicator();

        await dedup.ExecuteAsync<string>("key-1", () => Task.FromResult<string?>("a"));
        await dedup.ExecuteAsync<string>("key-2", () => Task.FromResult<string?>("b"));
        await dedup.ExecuteAsync<string>("key-3", () => Task.FromResult<string?>("c"));

        var stats = dedup.GetStatistics();
        stats.TotalRequests.Should().Be(3);
        stats.UniqueRequests.Should().Be(3);
        stats.DeduplicatedRequests.Should().Be(0);
    }

    #endregion

    #region ExecuteAsync — Basic Behavior

    [Fact]
    public async Task ExecuteAsync_ReturnsOperationResult()
    {
        var dedup = CreateDeduplicator();

        var result = await dedup.ExecuteAsync<string>("key-1", () => Task.FromResult<string?>("hello"));

        result.Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteAsync_NullResult_ReturnsNull()
    {
        var dedup = CreateDeduplicator();

        var result = await dedup.ExecuteAsync<string>("key-1", () => Task.FromResult<string?>(null));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_OperationThrows_PropagatesException()
    {
        var dedup = CreateDeduplicator();

        var act = () => dedup.ExecuteAsync<string>("key-1",
            () => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task ExecuteAsync_AfterFailure_KeyIsRemovedFromInflight()
    {
        var dedup = CreateDeduplicator();

        try
        {
            await dedup.ExecuteAsync<string>("key-1",
                () => throw new InvalidOperationException("fail"));
        }
        catch { /* expected */ }

        var stats = dedup.GetStatistics();
        stats.CurrentInflightRequests.Should().Be(0, "failed request should be removed from inflight");
    }

    #endregion

    #region ExecuteAsync — Deduplication (concurrent same key)

    [Fact]
    public async Task ExecuteAsync_ConcurrentSameKey_DeduplicatesAndSharesResult()
    {
        var dedup = CreateDeduplicator();
        var callCount = 0;
        var tcs = new TaskCompletionSource<string?>();

        // Start first request (blocks on tcs)
        var task1 = dedup.ExecuteAsync<string>("shared-key", async () =>
        {
            Interlocked.Increment(ref callCount);
            return await tcs.Task;
        });

        // Give task1 time to register in-flight
        await Task.Delay(50);

        // Start second request with same key — should deduplicate
        var task2 = dedup.ExecuteAsync<string>("shared-key", async () =>
        {
            Interlocked.Increment(ref callCount);
            return await tcs.Task;
        });

        // Release the first request
        tcs.SetResult("shared-result");

        var result1 = await task1;
        var result2 = await task2;

        result1.Should().Be("shared-result");
        result2.Should().Be("shared-result");
        // The operation should have been called only once (or at most — the second call deduplicates)
        callCount.Should().Be(1, "second call should have deduplicated onto first");

        var stats = dedup.GetStatistics();
        stats.TotalRequests.Should().Be(2);
        stats.DeduplicatedRequests.Should().Be(1);
        stats.UniqueRequests.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentDifferentKeys_ExecutesBoth()
    {
        var dedup = CreateDeduplicator();
        var callCount = 0;

        var task1 = dedup.ExecuteAsync<string>("key-A", () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<string?>("A");
        });

        var task2 = dedup.ExecuteAsync<string>("key-B", () =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult<string?>("B");
        });

        await Task.WhenAll(task1, task2);

        callCount.Should().Be(2, "different keys should not deduplicate");
    }

    #endregion

    #region ClearInflightRequests

    [Fact]
    public void ClearInflightRequests_ResetsInflightCount()
    {
        var dedup = CreateDeduplicator();

        // Nothing inflight, but calling clear should not throw
        dedup.ClearInflightRequests();

        dedup.GetStatistics().CurrentInflightRequests.Should().Be(0);
    }

    #endregion

    #region DeduplicationStatistics

    [Fact]
    public void DeduplicationRate_NoRequests_ReturnsZero()
    {
        var stats = new DeduplicationStatistics();

        stats.DeduplicationRate.Should().Be(0);
    }

    [Fact]
    public void DeduplicationRate_WithRequests_CalculatesPercentage()
    {
        var stats = new DeduplicationStatistics
        {
            TotalRequests = 10,
            DeduplicatedRequests = 3
        };

        stats.DeduplicationRate.Should().Be(30);
    }

    [Fact]
    public void DeduplicationRate_AllDeduplicated_Returns100()
    {
        var stats = new DeduplicationStatistics
        {
            TotalRequests = 5,
            DeduplicatedRequests = 5
        };

        stats.DeduplicationRate.Should().Be(100);
    }

    [Fact]
    public void DeduplicationStatistics_LastReset_DefaultsToUtcNow()
    {
        var before = DateTime.UtcNow;
        var stats = new DeduplicationStatistics();
        var after = DateTime.UtcNow;

        stats.LastReset.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    #endregion
}
