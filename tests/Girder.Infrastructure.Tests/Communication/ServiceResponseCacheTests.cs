using Infrastructure.Communication.Caching;
using Infrastructure.Communication.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class ServiceResponseCacheTests
{
    private readonly ILogger<ServiceResponseCache> _logger = Substitute.For<ILogger<ServiceResponseCache>>();

    private ServiceResponseCache CreateCache(IDistributedCache? cache = null)
    {
        var distributedCache = cache ?? new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        var options = Options.Create(new ServiceCommunicationOptions
        {
            Caching = new CacheConfiguration
            {
                DefaultTTL = TimeSpan.FromMinutes(5),
                EnableCompression = false
            }
        });

        return new ServiceResponseCache(distributedCache, _logger, options);
    }

    #region GetAsync

    [Fact]
    public async Task GetAsync_EmptyCache_ReturnsNull()
    {
        var cache = CreateCache();

        var result = await cache.GetAsync<TestData>("nonexistent-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_AfterSet_ReturnsCachedData()
    {
        var cache = CreateCache();
        var testData = new TestData { Name = "test", Value = 42 };

        await cache.SetAsync("key1", testData);
        var result = await cache.GetAsync<TestData>("key1");

        result.Should().NotBeNull();
        result!.Data.Name.Should().Be("test");
        result.Data.Value.Should().Be(42);
    }

    [Fact]
    public async Task GetAsync_ExpiredEntry_ReturnsNull()
    {
        var cache = CreateCache();
        var testData = new TestData { Name = "test", Value = 1 };

        await cache.SetAsync("key1", testData, ttl: TimeSpan.FromMilliseconds(1));

        // Wait for expiration
        await Task.Delay(50);

        var result = await cache.GetAsync<TestData>("key1");

        // The cache entry itself will expire via IDistributedCache,
        // or the CachedResponse.IsValid check will catch it
        // Either way, should return null
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_CacheThrows_ReturnsNull()
    {
        var failingCache = Substitute.For<IDistributedCache>();
        failingCache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache error"));

        var cache = CreateCache(failingCache);

        var result = await cache.GetAsync<TestData>("key1");

        result.Should().BeNull();
    }

    #endregion

    #region SetAsync

    [Fact]
    public async Task SetAsync_StoresData()
    {
        var cache = CreateCache();
        var testData = new TestData { Name = "stored", Value = 99 };

        await cache.SetAsync("set-key", testData);

        var result = await cache.GetAsync<TestData>("set-key");
        result.Should().NotBeNull();
        result!.Data.Name.Should().Be("stored");
    }

    [Fact]
    public async Task SetAsync_WithCustomTtl_UsesTtl()
    {
        var cache = CreateCache();
        var testData = new TestData { Name = "ttl", Value = 1 };

        await cache.SetAsync("ttl-key", testData, ttl: TimeSpan.FromHours(1));

        var result = await cache.GetAsync<TestData>("ttl-key");
        result.Should().NotBeNull();
        result!.ExpiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task SetAsync_WithEtag_StoresEtag()
    {
        var cache = CreateCache();
        var testData = new TestData { Name = "etag", Value = 1 };

        await cache.SetAsync("etag-key", testData, etag: "abc123");

        var result = await cache.GetAsync<TestData>("etag-key");
        result.Should().NotBeNull();
        result!.ETag.Should().Be("abc123");
    }

    [Fact]
    public async Task SetAsync_CacheThrows_DoesNotThrow()
    {
        var failingCache = Substitute.For<IDistributedCache>();
        failingCache.When(c => c.SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>()))
            .Throw(new InvalidOperationException("cache error"));

        var cache = CreateCache(failingCache);

        var act = () => cache.SetAsync("key", new TestData { Name = "test", Value = 1 });
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_ExistingKey_RemovesEntry()
    {
        var cache = CreateCache();
        var testData = new TestData { Name = "toremove", Value = 1 };
        await cache.SetAsync("rm-key", testData);

        await cache.RemoveAsync("rm-key");

        var result = await cache.GetAsync<TestData>("rm-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        var cache = CreateCache();
        var act = () => cache.RemoveAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_CacheThrows_DoesNotThrow()
    {
        var failingCache = Substitute.For<IDistributedCache>();
        failingCache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache error"));

        var cache = CreateCache(failingCache);

        var act = () => cache.RemoveAsync("key");
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RemoveByPatternAsync

    [Fact]
    public async Task RemoveByPatternAsync_DoesNotThrow()
    {
        var cache = CreateCache();
        var act = () => cache.RemoveByPatternAsync("test:*");
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_ExistingKey_ReturnsTrue()
    {
        var cache = CreateCache();
        await cache.SetAsync("exists-key", new TestData { Name = "test", Value = 1 });

        var exists = await cache.ExistsAsync("exists-key");

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_NonExistentKey_ReturnsFalse()
    {
        var cache = CreateCache();

        var exists = await cache.ExistsAsync("nonexistent");

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_CacheThrows_ReturnsFalse()
    {
        var failingCache = Substitute.For<IDistributedCache>();
        failingCache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("cache error"));

        var cache = CreateCache(failingCache);

        var result = await cache.ExistsAsync("key");

        result.Should().BeFalse();
    }

    #endregion

    #region GetStatistics

    [Fact]
    public void GetStatistics_Initial_AllZeros()
    {
        var cache = CreateCache();

        var stats = cache.GetStatistics();

        stats.TotalRequests.Should().Be(0);
        stats.CacheHits.Should().Be(0);
        stats.CacheMisses.Should().Be(0);
        stats.CacheEvictions.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_AfterMiss_IncrementsMisses()
    {
        var cache = CreateCache();

        await cache.GetAsync<TestData>("miss-key");

        var stats = cache.GetStatistics();
        stats.TotalRequests.Should().Be(1);
        stats.CacheMisses.Should().Be(1);
        stats.CacheHits.Should().Be(0);
    }

    [Fact]
    public async Task GetStatistics_AfterHit_IncrementsHits()
    {
        var cache = CreateCache();
        await cache.SetAsync("hit-key", new TestData { Name = "test", Value = 1 });

        await cache.GetAsync<TestData>("hit-key");

        var stats = cache.GetStatistics();
        stats.CacheHits.Should().Be(1);
    }

    [Fact]
    public async Task GetStatistics_AfterRemove_IncrementsEvictions()
    {
        var cache = CreateCache();
        await cache.SetAsync("evict-key", new TestData { Name = "test", Value = 1 });

        await cache.RemoveAsync("evict-key");

        var stats = cache.GetStatistics();
        stats.CacheEvictions.Should().Be(1);
    }

    #endregion

    #region CachedResponse DTO

    [Fact]
    public void CachedResponse_IsValid_NotExpired_ReturnsTrue()
    {
        var response = new CachedResponse<TestData>
        {
            Data = new TestData { Name = "test", Value = 1 },
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        response.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CachedResponse_IsValid_Expired_ReturnsFalse()
    {
        var response = new CachedResponse<TestData>
        {
            Data = new TestData { Name = "test", Value = 1 },
            CachedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1)
        };

        response.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CachedResponse_IsValid_NoExpiry_ReturnsTrue()
    {
        var response = new CachedResponse<TestData>
        {
            Data = new TestData { Name = "test", Value = 1 },
            CachedAt = DateTime.UtcNow,
            ExpiresAt = null
        };

        response.IsValid.Should().BeTrue();
    }

    #endregion

    #region CacheStatistics DTO

    [Fact]
    public void CacheStatistics_HitRate_CalculatesCorrectly()
    {
        var stats = new CacheStatistics
        {
            TotalRequests = 100,
            CacheHits = 75
        };

        stats.HitRate.Should().Be(75);
    }

    [Fact]
    public void CacheStatistics_HitRate_ZeroRequests_ReturnsZero()
    {
        var stats = new CacheStatistics();
        stats.HitRate.Should().Be(0);
    }

    #endregion

    public class TestData
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }
}
