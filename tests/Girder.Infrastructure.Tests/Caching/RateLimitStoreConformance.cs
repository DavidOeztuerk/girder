using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Caching;

/// <summary>
/// The behaviour every <see cref="IDistributedRateLimitStore"/> must show,
/// whatever it stores in. Derive one fixture per implementation.
/// </summary>
/// <remarks>
/// A rate limiter is only worth having if it holds under concurrency, and an
/// interface cannot express that. These tests are where the promise lives:
/// an implementation that compiles but lets every caller through at the limit
/// fails here rather than in production.
/// </remarks>
public abstract class RateLimitStoreConformance
{
    protected abstract IDistributedRateLimitStore CreateStore();

    private static string NewKey() => $"conformance:{Guid.NewGuid():N}";

    [Fact]
    public async Task An_unused_key_counts_zero()
    {
        (await CreateStore().GetCountAsync(NewKey())).Should().Be(0);
    }

    [Fact]
    public async Task Increment_counts_up_from_zero()
    {
        var store = CreateStore();
        var key = NewKey();

        (await store.IncrementAsync(key, TimeSpan.FromMinutes(1))).Should().Be(1);
        (await store.IncrementAsync(key, TimeSpan.FromMinutes(1))).Should().Be(2);
    }

    [Fact]
    public async Task A_key_exists_only_after_it_was_counted()
    {
        var store = CreateStore();
        var key = NewKey();

        (await store.ExistsAsync(key)).Should().BeFalse();
        await store.IncrementAsync(key, TimeSpan.FromMinutes(1));
        (await store.ExistsAsync(key)).Should().BeTrue();
    }

    [Fact]
    public async Task Delete_forgets_the_count()
    {
        var store = CreateStore();
        var key = NewKey();

        await store.IncrementAsync(key, TimeSpan.FromMinutes(1));
        await store.DeleteAsync(key);

        (await store.ExistsAsync(key)).Should().BeFalse();
        (await store.GetCountAsync(key)).Should().Be(0);
    }

    [Fact]
    public async Task Requests_below_the_limit_are_allowed()
    {
        var store = CreateStore();
        var key = NewKey();

        var first = await store.SlidingWindowIncrementAsync(key, limit: 3, TimeSpan.FromMinutes(1));

        first.IsAllowed.Should().BeTrue();
        first.CurrentCount.Should().Be(1);
        first.Limit.Should().Be(3);
        first.RemainingRequests.Should().Be(2);
    }

    [Fact]
    public async Task The_request_that_exceeds_the_limit_is_refused()
    {
        var store = CreateStore();
        var key = NewKey();

        for (var i = 0; i < 3; i++)
        {
            (await store.SlidingWindowIncrementAsync(key, limit: 3, TimeSpan.FromMinutes(1)))
                .IsAllowed.Should().BeTrue($"request {i + 1} of 3 is within the limit");
        }

        (await store.SlidingWindowIncrementAsync(key, limit: 3, TimeSpan.FromMinutes(1)))
            .IsAllowed.Should().BeFalse("the fourth request exceeds a limit of 3");
    }

    [Fact]
    public async Task Keys_are_counted_independently()
    {
        var store = CreateStore();
        var mine = NewKey();
        var theirs = NewKey();

        await store.SlidingWindowIncrementAsync(mine, limit: 1, TimeSpan.FromMinutes(1));

        (await store.SlidingWindowIncrementAsync(theirs, limit: 1, TimeSpan.FromMinutes(1)))
            .IsAllowed.Should().BeTrue("one caller's traffic must not spend another's budget");
    }

    [Fact]
    public async Task Counting_and_deciding_happen_as_one_step()
    {
        // The whole point of the port. Fifty callers race for ten slots; if
        // counting and deciding can interleave, more than ten get through.
        //
        // The gate is load-bearing: the store may complete synchronously, and
        // then Task.WhenAll over its tasks would run them one after another and
        // never produce a race at all. It is awaited rather than blocked on —
        // a Barrier would hold fifty pool threads and starve everything else in
        // the suite, including hosted services under test elsewhere.
        var store = CreateStore();
        var key = NewKey();
        const int limit = 10;
        const int callers = 50;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var racers = Enumerable.Range(0, callers).Select(async _ =>
        {
            await gate.Task;
            return await store.SlidingWindowIncrementAsync(key, limit, TimeSpan.FromMinutes(1));
        }).ToArray();

        gate.SetResult();
        var results = await Task.WhenAll(racers);

        results.Count(r => r.IsAllowed).Should().Be(limit);
    }

    [Fact]
    public async Task A_refused_request_does_not_consume_a_slot()
    {
        var store = CreateStore();
        var key = NewKey();

        await store.SlidingWindowIncrementAsync(key, limit: 1, TimeSpan.FromMinutes(1));
        var refused = await store.SlidingWindowIncrementAsync(key, limit: 1, TimeSpan.FromMinutes(1));

        refused.IsAllowed.Should().BeFalse();
        refused.CurrentCount.Should().Be(1, "a request that was turned away was not counted");
    }

    [Fact]
    public async Task The_window_forgets_what_fell_out_of_it()
    {
        var store = CreateStore();
        var key = NewKey();
        var window = TimeSpan.FromMilliseconds(150);

        (await store.SlidingWindowIncrementAsync(key, limit: 1, window)).IsAllowed.Should().BeTrue();
        (await store.SlidingWindowIncrementAsync(key, limit: 1, window)).IsAllowed.Should().BeFalse();

        await Task.Delay(window + TimeSpan.FromMilliseconds(150));

        (await store.SlidingWindowIncrementAsync(key, limit: 1, window))
            .IsAllowed.Should().BeTrue("the earlier request left the window");
    }
}

[Trait("Category", "Unit")]
public class InMemoryRateLimitStoreConformanceTests : RateLimitStoreConformance
{
    protected override IDistributedRateLimitStore CreateStore() =>
        new InMemoryRateLimitStore(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<InMemoryRateLimitStore>.Instance);
}
