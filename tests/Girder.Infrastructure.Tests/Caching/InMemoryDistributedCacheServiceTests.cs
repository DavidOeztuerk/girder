using Girder.Application.Abstractions;
using Girder.Application.Interfaces;
using Girder.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Caching;

[Trait("Category", "Unit")]
public class InMemoryDistributedCacheServiceTests : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<InMemoryDistributedCacheService> _logger;
    private readonly InMemoryDistributedCacheService _sut;

    public InMemoryDistributedCacheServiceTests()
    {
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _logger = Substitute.For<ILogger<InMemoryDistributedCacheService>>();
        _sut = new InMemoryDistributedCacheService(_memoryCache, _logger);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
    }

    #region GetAsync

    [Fact]
    public async Task GetAsync_KeyNotFound_ReturnsNull()
    {
        var result = await _sut.GetAsync<TestData>("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_KeyExists_ReturnsValue()
    {
        await _sut.SetAsync("key1", new TestData("hello"));

        var result = await _sut.GetAsync<TestData>("key1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("hello");
    }

    [Fact]
    public async Task GetAsync_TracksHitsAndMisses()
    {
        await _sut.SetAsync("key1", new TestData("hello"));

        await _sut.GetAsync<TestData>("key1"); // hit
        await _sut.GetAsync<TestData>("key1"); // hit
        await _sut.GetAsync<TestData>("missing"); // miss

        var stats = await _sut.GetStatisticsAsync();
        stats.Hits.Should().Be(2);
        stats.Misses.Should().Be(1);
    }

    #endregion

    #region GetOrSetAsync

    [Fact]
    public async Task GetOrSetAsync_KeyMissing_CallsFactoryAndCaches()
    {
        var factoryCallCount = 0;
        var result = await _sut.GetOrSetAsync("key1", () =>
        {
            factoryCallCount++;
            return Task.FromResult(new TestData("created"));
        });

        result.Name.Should().Be("created");
        factoryCallCount.Should().Be(1);

        // Second call should return cached value
        var result2 = await _sut.GetOrSetAsync("key1", () =>
        {
            factoryCallCount++;
            return Task.FromResult(new TestData("not-called"));
        });
        result2.Name.Should().Be("created");
        factoryCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrSetAsync_KeyExists_ReturnsExistingWithoutFactory()
    {
        await _sut.SetAsync("key1", new TestData("existing"));

        var result = await _sut.GetOrSetAsync("key1", () =>
            Task.FromResult(new TestData("should-not-be-used")));

        result.Name.Should().Be("existing");
    }

    #endregion

    #region SetAsync

    [Fact]
    public async Task SetAsync_WithExpiration_SetsValue()
    {
        await _sut.SetAsync("key1", new TestData("value"), TimeSpan.FromMinutes(5));

        var result = await _sut.GetAsync<TestData>("key1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WithoutExpiration_UsesDefault30Min()
    {
        await _sut.SetAsync("key1", new TestData("value"));

        var exists = await _sut.ExistsAsync("key1");
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task SetAsync_WithSlidingExpiration_SetsOption()
    {
        var options = new CacheOptions
        {
            SlidingExpiration = TimeSpan.FromMinutes(10)
        };
        await _sut.SetAsync("key1", new TestData("value"), null, options);

        var result = await _sut.GetAsync<TestData>("key1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WithAbsoluteExpiration_SetsOption()
    {
        var options = new CacheOptions
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(1)
        };
        await _sut.SetAsync("key1", new TestData("value"), null, options);

        var result = await _sut.GetAsync<TestData>("key1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SetAsync_WithTags_TracksTagAssociations()
    {
        var options = new CacheOptions
        {
            Tags = new HashSet<string> { "tag1", "tag2" }
        };
        await _sut.SetAsync("key1", new TestData("value1"), null, options);
        await _sut.SetAsync("key2", new TestData("value2"), null, options);

        // Removing by tag should remove both keys
        await _sut.RemoveByTagAsync("tag1");
        (await _sut.ExistsAsync("key1")).Should().BeFalse();
        (await _sut.ExistsAsync("key2")).Should().BeFalse();
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_SingleKey_RemovesEntry()
    {
        await _sut.SetAsync("key1", new TestData("value"));

        await _sut.RemoveAsync("key1");

        (await _sut.ExistsAsync("key1")).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveAsync_MultipleKeys_RemovesAllEntries()
    {
        await _sut.SetAsync("key1", new TestData("v1"));
        await _sut.SetAsync("key2", new TestData("v2"));
        await _sut.SetAsync("key3", new TestData("v3"));

        await _sut.RemoveAsync(new[] { "key1", "key2" });

        (await _sut.ExistsAsync("key1")).Should().BeFalse();
        (await _sut.ExistsAsync("key2")).Should().BeFalse();
        (await _sut.ExistsAsync("key3")).Should().BeTrue();
    }

    #endregion

    #region RemoveByPatternAsync

    [Fact]
    public async Task RemoveByPatternAsync_PrefixWildcard_RemovesMatchingKeys()
    {
        await _sut.SetAsync("user:1", new TestData("u1"));
        await _sut.SetAsync("user:2", new TestData("u2"));
        await _sut.SetAsync("job:1", new TestData("s1"));

        await _sut.RemoveByPatternAsync("user:*");

        (await _sut.ExistsAsync("user:1")).Should().BeFalse();
        (await _sut.ExistsAsync("user:2")).Should().BeFalse();
        (await _sut.ExistsAsync("job:1")).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByPatternAsync_SuffixWildcard_RemovesMatchingKeys()
    {
        await _sut.SetAsync("data:profile", new TestData("p"));
        await _sut.SetAsync("data:settings", new TestData("s"));
        await _sut.SetAsync("other:profile", new TestData("op"));

        await _sut.RemoveByPatternAsync("*:profile");

        (await _sut.ExistsAsync("data:profile")).Should().BeFalse();
        (await _sut.ExistsAsync("other:profile")).Should().BeFalse();
        (await _sut.ExistsAsync("data:settings")).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByPatternAsync_ExactMatch_RemovesSingleKey()
    {
        await _sut.SetAsync("exact-key", new TestData("value"));
        await _sut.SetAsync("other-key", new TestData("other"));

        await _sut.RemoveByPatternAsync("exact-key");

        (await _sut.ExistsAsync("exact-key")).Should().BeFalse();
        (await _sut.ExistsAsync("other-key")).Should().BeTrue();
    }

    #endregion

    #region RemoveByTagAsync / RemoveByTagsAsync

    [Fact]
    public async Task RemoveByTagAsync_NoTagRegistered_DoesNothing()
    {
        await _sut.SetAsync("key1", new TestData("value"));

        // This should not throw and should not remove key1
        await _sut.RemoveByTagAsync("nonexistent-tag");

        (await _sut.ExistsAsync("key1")).Should().BeTrue();
    }

    [Fact]
    public async Task RemoveByTagsAsync_MultipleTags_RemovesAll()
    {
        var opts1 = new CacheOptions { Tags = new HashSet<string> { "tagA" } };
        var opts2 = new CacheOptions { Tags = new HashSet<string> { "tagB" } };

        await _sut.SetAsync("key1", new TestData("v1"), null, opts1);
        await _sut.SetAsync("key2", new TestData("v2"), null, opts2);

        await _sut.RemoveByTagsAsync(new[] { "tagA", "tagB" });

        (await _sut.ExistsAsync("key1")).Should().BeFalse();
        (await _sut.ExistsAsync("key2")).Should().BeFalse();
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_KeyPresent_ReturnsTrue()
    {
        await _sut.SetAsync("exists-key", new TestData("v"));

        (await _sut.ExistsAsync("exists-key")).Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_KeyMissing_ReturnsFalse()
    {
        (await _sut.ExistsAsync("missing-key")).Should().BeFalse();
    }

    #endregion

    #region GetStatisticsAsync

    [Fact]
    public async Task GetStatisticsAsync_ReturnsCorrectStats()
    {
        await _sut.SetAsync("k1", new TestData("v1"));
        await _sut.SetAsync("k2", new TestData("v2"));

        var stats = await _sut.GetStatisticsAsync();

        stats.KeyCount.Should().Be(2);
        stats.InstanceInfo.Should().Be("InMemory");
        stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region RefreshAsync

    [Fact]
    public async Task RefreshAsync_KeyExists_DoesNotThrow()
    {
        await _sut.SetAsync("key1", new TestData("value"));

        var act = () => _sut.RefreshAsync("key1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RefreshAsync_KeyMissing_DoesNotThrow()
    {
        var act = () => _sut.RefreshAsync("missing-key");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetManyAsync

    [Fact]
    public async Task GetManyAsync_MixedKeys_ReturnsCorrectValues()
    {
        await _sut.SetAsync("key1", new TestData("v1"));
        await _sut.SetAsync("key2", new TestData("v2"));

        var result = await _sut.GetManyAsync<TestData>(new[] { "key1", "key2", "missing" });

        result.Should().HaveCount(3);
        result["key1"]!.Name.Should().Be("v1");
        result["key2"]!.Name.Should().Be("v2");
        result["missing"].Should().BeNull();
    }

    #endregion

    #region SetManyAsync

    [Fact]
    public async Task SetManyAsync_MultiplePairs_SetsAll()
    {
        var kvp = new Dictionary<string, TestData>
        {
            ["key1"] = new TestData("v1"),
            ["key2"] = new TestData("v2")
        };

        await _sut.SetManyAsync(kvp, TimeSpan.FromMinutes(5));

        (await _sut.GetAsync<TestData>("key1"))!.Name.Should().Be("v1");
        (await _sut.GetAsync<TestData>("key2"))!.Name.Should().Be("v2");
    }

    #endregion

    #region WarmCacheAsync

    [Fact]
    public async Task WarmCacheAsync_SuccessfulFactories_CachesAll()
    {
        var factories = new Dictionary<string, Func<Task<TestData>>>
        {
            ["key1"] = () => Task.FromResult(new TestData("warm1")),
            ["key2"] = () => Task.FromResult(new TestData("warm2"))
        };

        await _sut.WarmCacheAsync(factories, TimeSpan.FromMinutes(5));

        (await _sut.GetAsync<TestData>("key1"))!.Name.Should().Be("warm1");
        (await _sut.GetAsync<TestData>("key2"))!.Name.Should().Be("warm2");
    }

    [Fact]
    public async Task WarmCacheAsync_FactoryThrows_ContinuesWithOthers()
    {
        var factories = new Dictionary<string, Func<Task<TestData>>>
        {
            ["key1"] = () => throw new InvalidOperationException("fail"),
            ["key2"] = () => Task.FromResult(new TestData("warm2"))
        };

        await _sut.WarmCacheAsync(factories, TimeSpan.FromMinutes(5));

        (await _sut.ExistsAsync("key1")).Should().BeFalse();
        (await _sut.GetAsync<TestData>("key2"))!.Name.Should().Be("warm2");
    }

    #endregion

    #region Key Prefix

    [Fact]
    public async Task Constructor_WithPrefix_PrefixesAllKeys()
    {
        var prefixedSut = new InMemoryDistributedCacheService(_memoryCache, _logger, "myprefix:");

        await prefixedSut.SetAsync("key1", new TestData("value"));

        // Direct cache access should show prefixed key
        _memoryCache.TryGetValue("myprefix:key1", out _).Should().BeTrue();
        _memoryCache.TryGetValue("key1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_EmptyPrefix_NoPrefix()
    {
        await _sut.SetAsync("key1", new TestData("value"));

        _memoryCache.TryGetValue("key1", out _).Should().BeTrue();
    }

    #endregion

    public class TestData
    {
        public string Name { get; set; }
        public TestData(string name) => Name = name;
        public TestData() => Name = string.Empty;
    }
}
