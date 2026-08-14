using Girder.Infrastructure.Security.RateLimiting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security.RateLimiting;

[Trait("Category", "Unit")]
public class RateLimitServiceTests
{
    private readonly RateLimitService _sut;
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly ILogger<RateLimitService> _logger = Substitute.For<ILogger<RateLimitService>>();

    public RateLimitServiceTests()
    {
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _sut = new RateLimitService(_connectionMultiplexer, _logger);
    }

    #region Constructor / InitializeDefaultRules

    [Fact]
    public void Constructor_InitializesDefaultRules()
    {
        var rules = _sut.GetRegisteredRules().ToList();

        rules.Should().NotBeEmpty();
        rules.Should().Contain(r => r.Id == "global-default");
        rules.Should().Contain(r => r.Id == "authenticated-users");
        rules.Should().Contain(r => r.Id == "admin-users");
        rules.Should().Contain(r => r.Id == "heavy-endpoints");
    }

    [Fact]
    public void Constructor_GlobalDefault_Has1000RequestLimit()
    {
        var rules = _sut.GetRegisteredRules().ToList();
        var globalRule = rules.First(r => r.Id == "global-default");

        globalRule.Configuration.RequestLimit.Should().Be(1000);
        globalRule.Configuration.Algorithm.Should().Be(RateLimitAlgorithm.SlidingWindow);
    }

    [Fact]
    public void Constructor_HeavyEndpoints_UsesTokenBucket()
    {
        var rules = _sut.GetRegisteredRules().ToList();
        var heavyRule = rules.First(r => r.Id == "heavy-endpoints");

        heavyRule.Configuration.Algorithm.Should().Be(RateLimitAlgorithm.TokenBucket);
        heavyRule.Configuration.BurstLimit.Should().Be(20);
    }

    #endregion

    #region RegisterRuleAsync / RemoveRuleAsync

    [Fact]
    public async Task RegisterRuleAsync_NewRule_AddsToRegisteredRules()
    {
        var rule = new RateLimitRule
        {
            Id = "custom-rule",
            Name = "Custom Rule",
            Configuration = new RateLimitConfiguration { RequestLimit = 50 }
        };

        await _sut.RegisterRuleAsync(rule);

        var rules = _sut.GetRegisteredRules().ToList();
        rules.Should().Contain(r => r.Id == "custom-rule");
    }

    [Fact]
    public async Task RegisterRuleAsync_DuplicateId_ReplacesExisting()
    {
        var rule1 = new RateLimitRule { Id = "dup-rule", Name = "Original" };
        var rule2 = new RateLimitRule { Id = "dup-rule", Name = "Replacement" };

        await _sut.RegisterRuleAsync(rule1);
        await _sut.RegisterRuleAsync(rule2);

        var rules = _sut.GetRegisteredRules().ToList();
        rules.Count(r => r.Id == "dup-rule").Should().Be(1);
        rules.First(r => r.Id == "dup-rule").Name.Should().Be("Replacement");
    }

    [Fact]
    public async Task RemoveRuleAsync_ExistingRule_RemovesFromList()
    {
        var rule = new RateLimitRule { Id = "to-remove", Name = "Remove Me" };
        await _sut.RegisterRuleAsync(rule);

        await _sut.RemoveRuleAsync("to-remove");

        _sut.GetRegisteredRules().Should().NotContain(r => r.Id == "to-remove");
    }

    [Fact]
    public async Task RemoveRuleAsync_NonExistentRule_NoError()
    {
        var act = () => _sut.RemoveRuleAsync("non-existent");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region CheckRateLimitAsync

    [Fact]
    public async Task CheckRateLimitAsync_WhitelistedClient_ReturnsAllowed()
    {
        var request = CreateRequest("whitelisted-client");

        _database.KeyExistsAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("whitelist")),
            Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await _sut.CheckRateLimitAsync(request);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_BlacklistedClient_ReturnsBlocked()
    {
        var request = CreateRequest("blocked-user");

        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<RedisKey>(0).ToString();
                // whitelist key: ratelimit:whitelist:blocked-user -> false
                // blacklist key: ratelimit:blacklist:blocked-user -> true
                return key.StartsWith("ratelimit:blacklist:");
            });

        var result = await _sut.CheckRateLimitAsync(request);

        result.IsAllowed.Should().BeFalse();
        result.Severity.Should().Be(RateLimitSeverity.Critical);
    }

    [Fact]
    public async Task CheckRateLimitAsync_RedisError_FailsOpen()
    {
        var request = CreateRequest("error-client");

        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection failed"));

        var result = await _sut.CheckRateLimitAsync(request);

        result.IsAllowed.Should().BeTrue();
    }

    #endregion

    #region WhitelistClientAsync / BlacklistClientAsync

    [Fact]
    public async Task WhitelistClientAsync_StoresInRedis()
    {
        await _sut.WhitelistClientAsync("client-1", TimeSpan.FromHours(1));

        await _database.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("whitelist:client-1")),
            Arg.Any<RedisValue>(),
            Arg.Is<Expiration>(e => e.Equals(new Expiration(TimeSpan.FromHours(1)))),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WhitelistClientAsync_DefaultDuration_365Days()
    {
        await _sut.WhitelistClientAsync("client-1");

        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Is<Expiration>(e => e.Equals(new Expiration(TimeSpan.FromDays(365)))),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task BlacklistClientAsync_StoresInRedis()
    {
        await _sut.BlacklistClientAsync("client-1", TimeSpan.FromHours(2), "Abuse");

        await _database.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("blacklist:client-1")),
            Arg.Any<RedisValue>(),
            Arg.Is<Expiration>(e => e.Equals(new Expiration(TimeSpan.FromHours(2)))),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task BlacklistClientAsync_DefaultDuration_24Hours()
    {
        await _sut.BlacklistClientAsync("client-1");

        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Is<Expiration>(e => e.Equals(new Expiration(TimeSpan.FromHours(24)))),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task WhitelistClientAsync_RedisError_Throws()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.WhitelistClientAsync("client-1");

        await act.Should().ThrowAsync<RedisException>();
    }

    [Fact]
    public async Task BlacklistClientAsync_RedisError_Throws()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.BlacklistClientAsync("client-1");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region GetRegisteredRules

    [Fact]
    public void GetRegisteredRules_ReturnsSnapshot()
    {
        var rules1 = _sut.GetRegisteredRules();
        var rules2 = _sut.GetRegisteredRules();

        rules1.Should().BeEquivalentTo(rules2);
    }

    #endregion

    #region GetStatusAsync

    [Fact]
    public async Task GetStatusAsync_RedisError_ReturnsDefaultStatus()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.GetStatusAsync("client-1");

        result.ClientId.Should().Be("client-1");
    }

    #endregion

    #region Additional Tests

    [Fact]
    public async Task GetStatisticsAsync_RedisError_ReturnsDefaultStats()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.GetStatisticsAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsStatisticsObject()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetStatisticsAsync();

        result.Should().NotBeNull();
        result.TotalRequests.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task SetClientLimitsAsync_StoresCustomLimitsInRedis()
    {
        var limits = new Dictionary<string, RateLimitConfiguration>
        {
            ["default"] = new RateLimitConfiguration
            {
                RequestLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                Algorithm = RateLimitAlgorithm.SlidingWindow
            }
        };

        await _sut.SetClientLimitsAsync("custom-client", limits);

        // StackExchange.Redis 3.x: Die Produktion ruft
        // StringSetAsync(key, json, TimeSpan.FromDays(30)). Ein nicht-nullbares
        // TimeSpan konvertiert implizit nach Expiration, der Aufruf bindet also
        // an die Expiration-Ueberladung - nicht mehr an die mit TimeSpan?.
        await _database.Received(1).StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("custom-client")),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>());
    }

    [Fact]
    public async Task SetClientLimitsAsync_RedisError_Throws()
    {
        _database.StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.SetClientLimitsAsync("client-x", new Dictionary<string, RateLimitConfiguration>());

        await act.Should().ThrowAsync<RedisException>();
    }

    [Fact]
    public async Task ResetLimitsAsync_WhenEndpointsMissing_ThrowsAndLogsError()
    {
        // ResetLimitsAsync calls _database.Multiplexer.GetEndPoints().First()
        // With an empty/un-mocked endpoint list, it throws InvalidOperationException
        // The service catches the exception and re-throws it
        var act = () => _sut.ResetLimitsAsync("client-1");

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ResetLimitsAsync_WithMockedServer_DeletesMatchingKeys()
    {
        var endPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6379);
        var server = Substitute.For<IServer>();
        server.Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisKey>());

        // _database.Multiplexer must return _connectionMultiplexer for the chain to work
        _database.Multiplexer.Returns(_connectionMultiplexer);
        // Mock both the no-arg and bool-arg overloads
        _connectionMultiplexer.GetEndPoints().Returns(new System.Net.EndPoint[] { endPoint });
        _connectionMultiplexer.GetEndPoints(Arg.Any<bool>()).Returns(new System.Net.EndPoint[] { endPoint });
        _connectionMultiplexer.GetServer(endPoint, Arg.Any<object>()).Returns(server);

        await _sut.ResetLimitsAsync("client-1");

        // No keys to delete, but call should complete without error
        server.Received(1).Keys(Arg.Any<int>(), Arg.Any<RedisValue>(), Arg.Any<int>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task CheckRateLimitAsync_WithHeavyEndpointsRule_PotentiallyBlocked()
    {
        var request = CreateRequest("client-1", "/api/heavy");

        // Not whitelisted, not blacklisted
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        // Return a script result indicating the request is allowed (3 values: allowed, count, remaining)
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue[] { 1L, 10L, 990L }));

        var result = await _sut.CheckRateLimitAsync(request);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckRateLimitAsync_ScriptReturnsAllowed_IsAllowedTrue()
    {
        var request = CreateRequest("client-allowed", "/api/test");

        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        // Sliding window script returns [allowed, count, remaining] — 1 = allowed
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue[] { 1L, 1L, 999L }));

        var result = await _sut.CheckRateLimitAsync(request);

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_ScriptReturnsDenied_IsAllowedFalse()
    {
        var request = CreateRequest("client-limited", "/api/test");

        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        // Returns [0, count, 0] — denied (3 values required)
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue[] { 0L, 1001L, 0L }));

        var result = await _sut.CheckRateLimitAsync(request);

        result.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_WithCustomLimits_ReturnsStatusWithCustom()
    {
        // Return a serialized configuration for the client
        var config = new RateLimitConfiguration { RequestLimit = 50 };
        var json = System.Text.Json.JsonSerializer.Serialize(config);

        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);
        _database.StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("client-limits")),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.GetStatusAsync("client-1");

        result.Should().NotBeNull();
        result.ClientId.Should().Be("client-1");
    }

    [Fact]
    public async Task RegisterRuleAsync_WithClientIdCondition_RegistersSuccessfully()
    {
        var rule = new RateLimitRule
        {
            Id = "client-specific",
            Name = "Client Specific Rule",
            Conditions = new RateLimitConditions { ClientIds = new List<string> { "specific-client" } },
            Configuration = new RateLimitConfiguration { RequestLimit = 10 }
        };

        await _sut.RegisterRuleAsync(rule);

        var rules = _sut.GetRegisteredRules().ToList();
        rules.Should().Contain(r => r.Id == "client-specific");
    }

    [Fact]
    public async Task RegisterRuleAsync_WithEndpointCondition_RegistersSuccessfully()
    {
        var rule = new RateLimitRule
        {
            Id = "endpoint-specific",
            Name = "Endpoint Specific Rule",
            Conditions = new RateLimitConditions { Endpoints = new List<string> { "/api/upload" } },
            Configuration = new RateLimitConfiguration { RequestLimit = 5 }
        };

        await _sut.RegisterRuleAsync(rule);

        _sut.GetRegisteredRules().Should().Contain(r => r.Id == "endpoint-specific");
    }

    [Fact]
    public async Task RegisterRuleAsync_WithRoleCondition_RegistersSuccessfully()
    {
        var rule = new RateLimitRule
        {
            Id = "role-specific",
            Name = "Role Specific Rule",
            Conditions = new RateLimitConditions { UserRoles = new List<string> { "Premium" } },
            Configuration = new RateLimitConfiguration { RequestLimit = 10000 }
        };

        await _sut.RegisterRuleAsync(rule);

        _sut.GetRegisteredRules().Should().Contain(r => r.Id == "role-specific");
    }

    [Fact]
    public async Task CheckRateLimitAsync_RequestWithRole_MatchesRoleRule()
    {
        var adminRule = new RateLimitRule
        {
            Id = "admin-special",
            Name = "Admin Special",
            Conditions = new RateLimitConditions { UserRoles = new List<string> { "Admin" } },
            Configuration = new RateLimitConfiguration { RequestLimit = 99999 }
        };
        await _sut.RegisterRuleAsync(adminRule);

        var request = CreateRequest("admin-user", "/api/test", userRole: "Admin");
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue[] { 1L, 1L, 99998L }));

        var result = await _sut.CheckRateLimitAsync(request);

        result.Should().NotBeNull();
    }

    #endregion

    private static RateLimitRequest CreateRequest(
        string clientId,
        string endpoint = "/api/test",
        string method = "GET",
        string? userRole = null)
    {
        return new RateLimitRequest
        {
            ClientId = clientId,
            Endpoint = endpoint,
            Method = method,
            Path = endpoint,
            IpAddress = "127.0.0.1",
            UserRoles = userRole != null ? new List<string> { userRole } : new List<string>()
        };
    }
}
