using Girder.InMemory.Caching;
using Girder.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Caching;

[Trait("Category", "Unit")]
public class InMemoryRateLimitStoreTests : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<InMemoryRateLimitStore> _logger;
    private readonly InMemoryRateLimitStore _sut;

    public InMemoryRateLimitStoreTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<InMemoryRateLimitStore>>();
        _sut = new InMemoryRateLimitStore(_memoryCache, _logger);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
    }

    #region GetCountAsync

    [Fact]
    public async Task GetCountAsync_KeyMissing_ReturnsZero()
    {
        var count = await _sut.GetCountAsync("missing");

        count.Should().Be(0);
    }

    [Fact]
    public async Task GetCountAsync_KeyExists_ReturnsCount()
    {
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(5));
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(5));

        var count = await _sut.GetCountAsync("key1");

        count.Should().Be(2);
    }

    #endregion

    #region IncrementAsync

    [Fact]
    public async Task IncrementAsync_NewKey_ReturnsOne()
    {
        var result = await _sut.IncrementAsync("new-key", TimeSpan.FromMinutes(1));

        result.Should().Be(1);
    }

    [Fact]
    public async Task IncrementAsync_ExistingKey_Increments()
    {
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(1));
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(1));
        var result = await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(1));

        result.Should().Be(3);
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_KeyPresent_ReturnsTrue()
    {
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(1));

        (await _sut.ExistsAsync("key1")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_KeyMissing_ReturnsFalse()
    {
        (await _sut.ExistsAsync("missing")).Should().BeFalse();
    }

    #endregion

    #region ExpireAsync

    [Fact]
    public async Task ExpireAsync_KeyExists_ReturnsTrue()
    {
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(5));

        var result = await _sut.ExpireAsync("key1", TimeSpan.FromMinutes(10));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExpireAsync_KeyMissing_ReturnsFalse()
    {
        var result = await _sut.ExpireAsync("missing", TimeSpan.FromMinutes(10));

        result.Should().BeFalse();
    }

    #endregion

    #region GetTimeToLiveAsync

    [Fact]
    public async Task GetTimeToLiveAsync_AlwaysReturnsNull()
    {
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(5));

        var ttl = await _sut.GetTimeToLiveAsync("key1");

        ttl.Should().BeNull();
    }

    #endregion


    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_KeyExists_RemovesKeyAndReturnsTrue()
    {
        await _sut.IncrementAsync("key1", TimeSpan.FromMinutes(5));

        var result = await _sut.DeleteAsync("key1");

        result.Should().BeTrue();
        (await _sut.ExistsAsync("key1")).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_KeyMissing_ReturnsTrue()
    {
        // InMemoryRateLimitStore always returns true for delete
        var result = await _sut.DeleteAsync("missing");

        result.Should().BeTrue();
    }

    #endregion

    #region SlidingWindowIncrementAsync

    [Fact]
    public async Task SlidingWindowIncrementAsync_UnderLimit_IsAllowed()
    {
        var result = await _sut.SlidingWindowIncrementAsync("rate:key", 5, TimeSpan.FromMinutes(1));

        result.IsAllowed.Should().BeTrue();
        result.CurrentCount.Should().Be(1);
        result.Limit.Should().Be(5);
        result.ResetTime.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SlidingWindowIncrementAsync_AtLimit_IsDenied()
    {
        var window = TimeSpan.FromMinutes(1);
        var limit = 3;

        // Use up all allowed requests
        for (int i = 0; i < limit; i++)
        {
            var r = await _sut.SlidingWindowIncrementAsync("rate:key", limit, window);
            r.IsAllowed.Should().BeTrue();
        }

        // Next request should be denied
        var result = await _sut.SlidingWindowIncrementAsync("rate:key", limit, window);
        result.IsAllowed.Should().BeFalse();
        result.CurrentCount.Should().Be(limit);
    }

    [Fact]
    public async Task SlidingWindowIncrementAsync_RemainingRequests_CalculatedCorrectly()
    {
        var result = await _sut.SlidingWindowIncrementAsync("rate:key", 10, TimeSpan.FromMinutes(1));

        result.RemainingRequests.Should().Be(9);
    }

    #endregion
}
