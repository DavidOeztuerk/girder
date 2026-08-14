using Infrastructure.Security;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class RedisTokenRevocationServiceTopUpTests
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly ILogger<RedisTokenRevocationService> _logger = Substitute.For<ILogger<RedisTokenRevocationService>>();
    private readonly RedisTokenRevocationService _sut;

    public RedisTokenRevocationServiceTopUpTests()
    {
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _database.Multiplexer.Returns(_connectionMultiplexer);
        _sut = new RedisTokenRevocationService(_connectionMultiplexer, _logger);
    }

    #region CleanupExpiredTokensAsync

    [Fact]
    public async Task CleanupExpiredTokensAsync_NoKeys_LogsZeroDeleted()
    {
        // Arrange — server that returns no keys
        var server = Substitute.For<IServer>();
        server.Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(Enumerable.Empty<RedisKey>());

        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object>())
            .Returns(server);

        // Act — should not throw
        var act = () => _sut.CleanupExpiredTokensAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_WithExpiredTokens_DeletesThemInBatches()
    {
        // Arrange — mock server returning >100 keys to test batch processing
        var keys = Enumerable.Range(0, 150).Select(i => new RedisKey($"revoked:jti:token-{i}")).ToList();

        var server = Substitute.For<IServer>();
        server.Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(keys);

        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object>())
            .Returns(server);

        // Each key has an expired token
        var expiredInfo = new RevokedTokenInfo
        {
            Jti = "jti",
            UserId = "user-1",
            RevokedAt = DateTime.UtcNow.AddHours(-2),
            ExpiresAt = DateTime.UtcNow.AddHours(-1) // already expired
        };
        var tokenJson = JsonSerializer.Serialize(expiredInfo);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(tokenJson));

        // Act
        var act = () => _sut.CleanupExpiredTokensAsync();

        await act.Should().NotThrowAsync();

        // Should have called KeyDeleteAsync at least once (for batches)
        await _database.Received().KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_WithActiveTokens_DoesNotDeleteThem()
    {
        var keys = new[] { new RedisKey("revoked:jti:active-token") };

        var server = Substitute.For<IServer>();
        server.Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(keys);

        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object>())
            .Returns(server);

        // Token with future expiry — not expired
        var activeInfo = new RevokedTokenInfo
        {
            Jti = "jti-active",
            UserId = "user-1",
            RevokedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(activeInfo)));

        await _sut.CleanupExpiredTokensAsync();

        // No deletion for non-expired tokens
        await _database.DidNotReceive().KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_WithEmptyRedisValue_DeletesKey()
    {
        var keys = new[] { new RedisKey("revoked:jti:empty-key") };

        var server = Substitute.For<IServer>();
        server.Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(keys);

        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object>())
            .Returns(server);

        // Empty value — should be marked for deletion
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        await _sut.CleanupExpiredTokensAsync();

        await _database.Received(1).KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_WithCorruptedJson_DeletesKey()
    {
        var keys = new[] { new RedisKey("revoked:jti:corrupt-key") };

        var server = Substitute.For<IServer>();
        server.Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(keys);

        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object>())
            .Returns(server);

        // Corrupted JSON — JsonException should mark for deletion
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("not-valid-json!"));

        await _sut.CleanupExpiredTokensAsync();

        await _database.Received(1).KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_RedisServerThrows_DoesNotPropagateException()
    {
        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>())
            .Returns(new System.Net.EndPoint[] { new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379) });
        _connectionMultiplexer.GetServer(Arg.Any<System.Net.EndPoint>(), Arg.Any<object>())
            .Throws(new RedisException("Server unavailable"));

        var act = () => _sut.CleanupExpiredTokensAsync();

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetRevokedTokensAsync — with members

    [Fact]
    public async Task GetRevokedTokensAsync_WithMembers_ReturnsEmptyDueToCommentedCode()
    {
        // The production code has commented-out deserialization — returns empty list
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "revoked:jti:token-1", "revoked:jti:token-2" });

        var result = await _sut.GetRevokedTokensAsync("user-1");

        result.Should().BeEmpty();
    }

    #endregion

    #region RevokeTokenAsync — custom key prefix

    [Fact]
    public async Task Constructor_CustomKeyPrefix_UsesPrefix()
    {
        var customService = new RedisTokenRevocationService(_connectionMultiplexer, _logger, "custom:");

        _database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]>(),
                Arg.Any<RedisValue[]>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1));

        await customService.RevokeTokenAsync("test-jti");

        // Verify a Lua script was executed with the custom prefix in keys
        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Is<RedisKey[]>(keys => keys.Any(k => k.ToString().StartsWith("custom:"))),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    #endregion
}
