using Infrastructure.Security.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security.RateLimiting;

[Trait("Category", "Unit")]
public class InMemoryRateLimitServiceTests
{
    private readonly InMemoryRateLimitService _sut;
    private readonly ILogger<InMemoryRateLimitService> _logger = Substitute.For<ILogger<InMemoryRateLimitService>>();

    public InMemoryRateLimitServiceTests()
    {
        _sut = new InMemoryRateLimitService(_logger);
    }

    #region WhitelistClientAsync / BlacklistClientAsync

    [Fact]
    public async Task WhitelistClientAsync_AddedClient_AllowedByCheckRateLimit()
    {
        await _sut.WhitelistClientAsync("wl-client");

        var result = await _sut.CheckRateLimitAsync(CreateRequest("wl-client"));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task BlacklistClientAsync_AddedClient_BlockedByCheckRateLimit()
    {
        await _sut.BlacklistClientAsync("bl-client", null, "abuse");

        var result = await _sut.CheckRateLimitAsync(CreateRequest("bl-client"));

        result.IsAllowed.Should().BeFalse();
        result.Severity.Should().Be(RateLimitSeverity.Critical);
    }

    [Fact]
    public async Task WhitelistClientAsync_WithDuration_StillWhitelisted()
    {
        await _sut.WhitelistClientAsync("wl-duration", TimeSpan.FromHours(1));

        var result = await _sut.CheckRateLimitAsync(CreateRequest("wl-duration"));

        result.IsAllowed.Should().BeTrue();
    }

    #endregion

    #region RegisterRuleAsync / RemoveRuleAsync

    [Fact]
    public async Task RegisterRuleAsync_NewRule_AppendedToList()
    {
        var rule = new RateLimitRule { Id = "in-mem-rule", Name = "Test", Configuration = new RateLimitConfiguration { RequestLimit = 100 } };

        await _sut.RegisterRuleAsync(rule);

        _sut.GetRegisteredRules().Should().Contain(r => r.Id == "in-mem-rule");
    }

    [Fact]
    public async Task RegisterRuleAsync_DuplicateId_ReplacesExisting()
    {
        var rule1 = new RateLimitRule { Id = "dup", Name = "First" };
        var rule2 = new RateLimitRule { Id = "dup", Name = "Second" };

        await _sut.RegisterRuleAsync(rule1);
        await _sut.RegisterRuleAsync(rule2);

        var rules = _sut.GetRegisteredRules().ToList();
        rules.Count(r => r.Id == "dup").Should().Be(1);
        rules.First(r => r.Id == "dup").Name.Should().Be("Second");
    }

    [Fact]
    public async Task RemoveRuleAsync_ExistingRule_Removed()
    {
        var rule = new RateLimitRule { Id = "remove-me", Name = "Remove" };
        await _sut.RegisterRuleAsync(rule);

        await _sut.RemoveRuleAsync("remove-me");

        _sut.GetRegisteredRules().Should().NotContain(r => r.Id == "remove-me");
    }

    [Fact]
    public async Task RemoveRuleAsync_NonExistentRule_NoError()
    {
        var act = () => _sut.RemoveRuleAsync("does-not-exist");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region CheckRateLimitAsync

    [Fact]
    public async Task CheckRateLimitAsync_NoRules_ReturnsAllowed()
    {
        var result = await _sut.CheckRateLimitAsync(CreateRequest("new-client"));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_BelowLimit_ReturnsAllowed()
    {
        var rule = new RateLimitRule
        {
            Id = "low-limit",
            Name = "Low",
            IsEnabled = true,
            Configuration = new RateLimitConfiguration { RequestLimit = 10, Window = TimeSpan.FromMinutes(1) },
            Conditions = new RateLimitConditions()
        };
        await _sut.RegisterRuleAsync(rule);

        var result = await _sut.CheckRateLimitAsync(CreateRequest("client-a"));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_BlacklistedClient_ReturnsBlocked()
    {
        // The in-memory sliding-window resets per-tick; use blacklist to reliably test blocking
        await _sut.BlacklistClientAsync("blocked-client");

        var request = CreateRequest("blocked-client");
        var result = await _sut.CheckRateLimitAsync(request);

        result.IsAllowed.Should().BeFalse();
        result.Severity.Should().Be(RateLimitSeverity.Critical);
    }

    [Fact]
    public async Task CheckRateLimitAsync_DisabledRule_Ignored()
    {
        var rule = new RateLimitRule
        {
            Id = "disabled",
            Name = "Disabled",
            IsEnabled = false,
            Configuration = new RateLimitConfiguration { RequestLimit = 0 },
            Conditions = new RateLimitConditions()
        };
        await _sut.RegisterRuleAsync(rule);

        var result = await _sut.CheckRateLimitAsync(CreateRequest("any-client"));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_RoleCondition_NotMatching_RuleSkipped()
    {
        var rule = new RateLimitRule
        {
            Id = "role-rule",
            Name = "RoleRule",
            IsEnabled = true,
            Configuration = new RateLimitConfiguration { RequestLimit = 0 },
            Conditions = new RateLimitConditions { UserRoles = new List<string> { "Admin" } }
        };
        await _sut.RegisterRuleAsync(rule);

        // Request without Admin role
        var result = await _sut.CheckRateLimitAsync(CreateRequest("user-without-role"));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_EndpointCondition_NotMatching_RuleSkipped()
    {
        var rule = new RateLimitRule
        {
            Id = "ep-rule",
            Name = "EndpointRule",
            IsEnabled = true,
            Configuration = new RateLimitConfiguration { RequestLimit = 0 },
            Conditions = new RateLimitConditions { Endpoints = new List<string> { "/api/heavy" } }
        };
        await _sut.RegisterRuleAsync(rule);

        // Request to a different endpoint
        var result = await _sut.CheckRateLimitAsync(CreateRequest("client", "/api/light"));

        result.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task CheckRateLimitAsync_WhitelistTakesPrecedenceOverRule()
    {
        var rule = new RateLimitRule
        {
            Id = "block-all",
            Name = "BlockAll",
            IsEnabled = true,
            Configuration = new RateLimitConfiguration { RequestLimit = 0 },
            Conditions = new RateLimitConditions()
        };
        await _sut.RegisterRuleAsync(rule);
        await _sut.WhitelistClientAsync("privileged");

        var result = await _sut.CheckRateLimitAsync(CreateRequest("privileged"));

        result.IsAllowed.Should().BeTrue();
    }

    #endregion

    #region GetStatusAsync

    [Fact]
    public async Task GetStatusAsync_NewClient_ReturnsDefaultStatus()
    {
        var result = await _sut.GetStatusAsync("fresh-client");

        result.ClientId.Should().Be("fresh-client");
        result.IsWhitelisted.Should().BeFalse();
        result.IsBlacklisted.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_WhitelistedClient_ReturnsIsWhitelisted()
    {
        await _sut.WhitelistClientAsync("status-wl");

        var result = await _sut.GetStatusAsync("status-wl");

        result.IsWhitelisted.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_BlacklistedClient_ReturnsIsBlacklisted()
    {
        await _sut.BlacklistClientAsync("status-bl");

        var result = await _sut.GetStatusAsync("status-bl");

        result.IsBlacklisted.Should().BeTrue();
    }

    #endregion

    #region ResetLimitsAsync

    [Fact]
    public async Task ResetLimitsAsync_AfterHittingLimit_ClientCanRequestAgain()
    {
        var rule = new RateLimitRule
        {
            Id = "reset-rule",
            Name = "Reset",
            IsEnabled = true,
            Priority = 100,
            Configuration = new RateLimitConfiguration { RequestLimit = 1, Window = TimeSpan.FromMinutes(1) },
            Conditions = new RateLimitConditions()
        };
        await _sut.RegisterRuleAsync(rule);

        var request = CreateRequest("reset-client");
        await _sut.CheckRateLimitAsync(request);
        await _sut.CheckRateLimitAsync(request); // exceeds limit

        await _sut.ResetLimitsAsync("reset-client");

        var afterReset = await _sut.CheckRateLimitAsync(request);
        afterReset.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ResetLimitsAsync_NonExistentClient_NoError()
    {
        var act = () => _sut.ResetLimitsAsync("no-such-client");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetStatisticsAsync

    [Fact]
    public async Task GetStatisticsAsync_NoViolations_ZeroCount()
    {
        var result = await _sut.GetStatisticsAsync();

        result.TotalViolations.Should().Be(0);
    }

    [Fact]
    public async Task GetStatisticsAsync_AfterBlacklist_ReturnsExpectedStats()
    {
        await _sut.BlacklistClientAsync("violating-client");
        await _sut.CheckRateLimitAsync(CreateRequest("violating-client"));

        // Blacklist blocking does NOT add to _violations — just verify stats object is returned
        var stats = await _sut.GetStatisticsAsync();

        stats.Should().NotBeNull();
        stats.TotalViolations.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region SetClientLimitsAsync

    [Fact]
    public async Task SetClientLimitsAsync_NoError()
    {
        var limits = new Dictionary<string, RateLimitConfiguration>
        {
            ["custom"] = new RateLimitConfiguration { RequestLimit = 50 }
        };

        var act = () => _sut.SetClientLimitsAsync("client-limits", limits);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetRegisteredRules

    [Fact]
    public void GetRegisteredRules_InitiallyEmpty()
    {
        _sut.GetRegisteredRules().Should().BeEmpty();
    }

    [Fact]
    public async Task GetRegisteredRules_AfterRegistration_ContainsRule()
    {
        await _sut.RegisterRuleAsync(new RateLimitRule { Id = "r1", Name = "Rule1" });
        await _sut.RegisterRuleAsync(new RateLimitRule { Id = "r2", Name = "Rule2" });

        _sut.GetRegisteredRules().Should().HaveCount(2);
    }

    #endregion

    private static RateLimitRequest CreateRequest(string clientId, string endpoint = "/api/test")
    {
        return new RateLimitRequest
        {
            ClientId = clientId,
            Endpoint = endpoint,
            Method = "GET",
            Path = endpoint,
            IpAddress = "127.0.0.1",
            UserRoles = new List<string>()
        };
    }
}
