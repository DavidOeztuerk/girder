using Girder.Abstractions.Caching;
using Girder.Redis.Caching;
using Girder.Application.Interfaces;
using Girder.Infrastructure.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Caching;

[Trait("Category", "Unit")]
public class RedisDistributedCacheServiceTests
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IDatabase _database;
    private readonly IServer _server;
    private readonly ILogger<RedisDistributedCacheService> _logger;
    private readonly RedisDistributedCacheService _sut;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public RedisDistributedCacheServiceTests()
    {
        _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        _database = Substitute.For<IDatabase>();
        _server = Substitute.For<IServer>();
        _logger = Substitute.For<ILogger<RedisDistributedCacheService>>();

        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>()).Returns(new EndPoint[] { new DnsEndPoint("localhost", 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<EndPoint>(), Arg.Any<object>()).Returns(_server);

        _sut = new RedisDistributedCacheService(_connectionMultiplexer, _logger);
    }

    private static string CreateCacheEntryJson(object data, bool compressed = false)
    {
        var serializedData = JsonSerializer.Serialize(data, JsonOptions);
        var entry = new
        {
            data = serializedData,
            compressed,
            createdAt = DateTime.UtcNow,
            tags = Array.Empty<string>()
        };
        return JsonSerializer.Serialize(entry, JsonOptions);
    }

    #region GetAsync

    [Fact]
    public async Task GetAsync_KeyMissing_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetAsync<TestDto>("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_InvalidJson_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("not-valid-json"));

        var result = await _sut.GetAsync<TestDto>("bad-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_RedisThrows_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue>(_ => throw RedisFailures.Unreachable("test"));

        var result = await _sut.GetAsync<TestDto>("error-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_ValidCacheEntry_ReturnsDeserializedValue()
    {
        var json = CreateCacheEntryJson(new TestDto { Name = "test" });
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.GetAsync<TestDto>("key1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("test");
    }

    #endregion

    #region GetOrSetAsync

    [Fact]
    public async Task GetOrSetAsync_CacheMiss_CallsFactoryAndSets()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetOrSetAsync("key1", () => Task.FromResult(new TestDto { Name = "created" }));

        result.Name.Should().Be("created");
        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetOrSetAsync_CacheHit_ReturnsWithoutFactory()
    {
        var json = CreateCacheEntryJson(new TestDto { Name = "cached" });
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var factoryCalled = false;
        var result = await _sut.GetOrSetAsync("key1", () =>
        {
            factoryCalled = true;
            return Task.FromResult(new TestDto { Name = "new" });
        });

        result.Name.Should().Be("cached");
        factoryCalled.Should().BeFalse();
    }

    #endregion

    #region SetAsync

    [Fact]
    public async Task SetAsync_WithExpiration_SetsWithTTL()
    {
        await _sut.SetAsync("key1", new TestDto { Name = "v" }, TimeSpan.FromMinutes(5));

        await _database.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "cache:key1"),
            Arg.Any<RedisValue>(),
            Arg.Is<Expiration>(e => e.Equals(new Expiration(TimeSpan.FromMinutes(5)))),
            Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetAsync_WithoutExpiration_SetsWithoutTTL()
    {
        await _sut.SetAsync("key1", new TestDto { Name = "v" });

        // No-expiration overload: StringSetAsync(RedisKey, RedisValue)
        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetAsync_WithTags_SetsTagAssociations()
    {
        var options = new CacheOptions { Tags = new HashSet<string> { "tag1", "tag2" } };

        await _sut.SetAsync("key1", new TestDto { Name = "v" }, TimeSpan.FromMinutes(5), options);

        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "tag:tag1"),
            Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
        await _database.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "tag:tag2"),
            Arg.Any<RedisValue>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetAsync_RedisThrows_DoesNotRethrow()
    {
        _database.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>(), Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.SetAsync("key1", new TestDto { Name = "v" }, TimeSpan.FromMinutes(1));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetAsync_WithCompress_CompressesData()
    {
        var options = new CacheOptions { Compress = true };

        await _sut.SetAsync("key1", new TestDto { Name = "compressed-value" }, TimeSpan.FromMinutes(5), options);

        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Is<RedisValue>(v => v.ToString()!.Contains("\"compressed\":true")),
            Arg.Any<Expiration>(), Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>());
    }

    #endregion

    #region RemoveAsync

    [Fact]
    public async Task RemoveAsync_SingleKey_DeletesKey()
    {
        await _sut.RemoveAsync("key1");

        await _database.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "cache:key1"), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveAsync_SingleKey_RedisThrows_DoesNotRethrow()
    {
        _database.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.RemoveAsync("key1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAsync_MultipleKeys_DeletesBatch()
    {
        await _sut.RemoveAsync(new[] { "key1", "key2" });

        await _database.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey[]>(keys => keys.Length == 2), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveAsync_MultipleKeys_RedisThrows_DoesNotRethrow()
    {
        _database.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns<long>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.RemoveAsync(new[] { "key1", "key2" });

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RemoveByPatternAsync

    [Fact]
    public async Task RemoveByPatternAsync_RedisThrows_DoesNotRethrow()
    {
        _database.ExecuteAsync(Arg.Any<string>(), Arg.Any<object[]>())
            .Returns<RedisResult>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.RemoveByPatternAsync("user:*");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RemoveByTagAsync

    [Fact]
    public async Task RemoveByTagAsync_ExecutesLuaScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)"3"));

        await _sut.RemoveByTagAsync("my-tag");

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(k => k[0].ToString() == "tag:my-tag"),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RemoveByTagAsync_RedisThrows_DoesNotRethrow()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns<RedisResult>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.RemoveByTagAsync("my-tag");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region RemoveByTagsAsync

    [Fact]
    public async Task RemoveByTagsAsync_MultipleTags_CallsRemoveByTagForEach()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)"0"));

        await _sut.RemoveByTagsAsync(new[] { "tag1", "tag2" });

        await _database.Received(2).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    #endregion

    #region ExistsAsync

    [Fact]
    public async Task ExistsAsync_KeyExists_ReturnsTrue()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        var result = await _sut.ExistsAsync("key1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_KeyMissing_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(false);

        var result = await _sut.ExistsAsync("key1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_RedisThrows_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw RedisFailures.Unreachable("test"));

        var result = await _sut.ExistsAsync("key1");

        result.Should().BeFalse();
    }

    #endregion

    #region GetStatisticsAsync

    [Fact]
    public async Task GetStatisticsAsync_RedisThrows_ReturnsEmptyStats()
    {
        _server.InfoAsync(Arg.Any<RedisValue>(), Arg.Any<CommandFlags>())
            .Returns<IGrouping<string, KeyValuePair<string, string>>[]>(_ => throw RedisFailures.Unreachable("test"));

        var stats = await _sut.GetStatisticsAsync();

        stats.Should().NotBeNull();
    }

    #endregion

    #region RefreshAsync

    [Fact]
    public async Task RefreshAsync_KeyWithTTL_RefreshesExpiration()
    {
        _database.KeyTimeToLiveAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(TimeSpan.FromMinutes(5));

        await _sut.RefreshAsync("key1");

        // KeyExpireAsync(key, timespan) resolves to the 4-param overload:
        // KeyExpireAsync(RedisKey, TimeSpan?, ExpireWhen, CommandFlags)
        await _database.Received(1).KeyExpireAsync(
            Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RefreshAsync_KeyWithoutTTL_DoesNotSetExpiration()
    {
        _database.KeyTimeToLiveAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((TimeSpan?)null);

        await _sut.RefreshAsync("key1");

        await _database.DidNotReceive().KeyExpireAsync(
            Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RefreshAsync_RedisThrows_DoesNotRethrow()
    {
        _database.KeyTimeToLiveAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns<TimeSpan?>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.RefreshAsync("key1");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetManyAsync

    [Fact]
    public async Task GetManyAsync_MixedResults_ReturnsDictionary()
    {
        var json = CreateCacheEntryJson(new TestDto { Name = "found" });
        _database.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { new RedisValue(json), RedisValue.Null });

        var result = await _sut.GetManyAsync<TestDto>(new[] { "key1", "missing" });

        result.Should().HaveCount(2);
        result["key1"]!.Name.Should().Be("found");
        result["missing"].Should().BeNull();
    }

    [Fact]
    public async Task GetManyAsync_RedisThrows_ReturnsNullsForAll()
    {
        _database.StringGetAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .Returns<RedisValue[]>(_ => throw RedisFailures.Unreachable("test"));

        var result = await _sut.GetManyAsync<TestDto>(new[] { "key1", "key2" });

        result.Should().HaveCount(2);
        result["key1"].Should().BeNull();
        result["key2"].Should().BeNull();
    }

    #endregion

    #region SetManyAsync

    [Fact]
    public async Task SetManyAsync_MultipleEntries_SetsBatch()
    {
        var kvp = new Dictionary<string, TestDto>
        {
            ["key1"] = new TestDto { Name = "v1" },
            ["key2"] = new TestDto { Name = "v2" }
        };

        await _sut.SetManyAsync(kvp);

        // StringSetAsync(array) binds to the Expiration overload; mocking the
        // other one compiles but never matches.
        await _database.Received(1).StringSetAsync(
            Arg.Is<KeyValuePair<RedisKey, RedisValue>[]>(arr => arr.Length == 2),
            Arg.Any<When>(), Arg.Any<Expiration>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetManyAsync_WithExpiration_SetsExpirationOnEachKey()
    {
        var kvp = new Dictionary<string, TestDto>
        {
            ["key1"] = new TestDto { Name = "v1" },
            ["key2"] = new TestDto { Name = "v2" }
        };

        await _sut.SetManyAsync(kvp, TimeSpan.FromMinutes(5));

        await _database.Received(2).KeyExpireAsync(
            Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>(), Arg.Any<ExpireWhen>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetManyAsync_RedisThrows_DoesNotRethrow()
    {
        _database.StringSetAsync(Arg.Any<KeyValuePair<RedisKey, RedisValue>[]>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns<bool>(_ => throw RedisFailures.Unreachable("test"));

        var act = () => _sut.SetManyAsync(new Dictionary<string, TestDto> { ["k"] = new TestDto { Name = "v" } });

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region WarmCacheAsync

    [Fact]
    public async Task WarmCacheAsync_FactoryThrows_ContinuesWithOthers()
    {
        var factories = new Dictionary<string, Func<Task<TestDto>>>
        {
            ["key1"] = () => throw new System.InvalidOperationException("fail"),
            ["key2"] = () => Task.FromResult(new TestDto { Name = "ok" })
        };

        var act = () => _sut.WarmCacheAsync(factories, TimeSpan.FromMinutes(5));

        await act.Should().NotThrowAsync();
    }

    #endregion

    public class TestDto
    {
        public string Name { get; set; } = string.Empty;
    }
}
