using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class RedisTokenRevocationServiceTests
{
    private readonly IDatabase _database;
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisTokenRevocationService> _logger;
    private readonly RedisTokenRevocationService _service;

    public RedisTokenRevocationServiceTests()
    {
        _database = Substitute.For<IDatabase>();
        _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _logger = Substitute.For<ILogger<RedisTokenRevocationService>>();
        _service = new RedisTokenRevocationService(_connectionMultiplexer, _logger);
    }

    [Fact]
    public async Task RevokeTokenAsync_ByJti_ExecutesLuaScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1));

        await _service.RevokeTokenAsync("test-jti", TimeSpan.FromMinutes(30));

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokeTokenAsync_WithRequest_ExecutesLuaScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create((RedisValue)1));

        var request = new TokenRevocationRequest
        {
            Jti = "jti-test",
            UserId = "user-1",
            Reason = TokenRevocationReason.SecurityIncident
        };

        await _service.RevokeTokenAsync(request);

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokeTokenAsync_RedisError_Throws()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection failed"));

        var act = () => _service.RevokeTokenAsync("jti-test", TimeSpan.FromMinutes(30));

        await act.Should().ThrowAsync<RedisException>();
    }

    [Fact]
    public async Task IsTokenRevokedAsync_TokenExists_ReturnsTrue()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await _service.IsTokenRevokedAsync("revoked-jti");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsTokenRevokedAsync_TokenNotExists_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        var result = await _service.IsTokenRevokedAsync("valid-jti");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsTokenRevokedAsync_RedisError_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection failed"));

        var result = await _service.IsTokenRevokedAsync("some-jti");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_SetsKeyInRedis()
    {
        await _service.RevokeRefreshTokenAsync("refresh-token-abc");

        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_RedisError_Throws()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var act = () => _service.RevokeRefreshTokenAsync("refresh-token");

        await act.Should().ThrowAsync<RedisException>();
    }

    [Fact]
    public async Task IsRefreshTokenRevokedAsync_Exists_ReturnsTrue()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await _service.IsRefreshTokenRevokedAsync("revoked-refresh");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRefreshTokenRevokedAsync_NotExists_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        var result = await _service.IsRefreshTokenRevokedAsync("valid-refresh");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRefreshTokenRevokedAsync_RedisError_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var result = await _service.IsRefreshTokenRevokedAsync("some-refresh");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetRevokedTokensAsync_ReturnsEmptyOnError()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var result = await _service.GetRevokedTokensAsync("user-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRevokedTokensAsync_WithTokenKeys_ReturnsEmptyList()
    {
        // The current implementation has commented-out deserialization code
        // so it returns an empty list for each token key
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "revoked:jti:test-1" });

        var result = (await _service.GetRevokedTokensAsync("user-1")).ToList();

        result.Should().BeEmpty();
    }
}
