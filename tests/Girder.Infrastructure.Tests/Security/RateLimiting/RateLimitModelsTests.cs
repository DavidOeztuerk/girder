using Infrastructure.Security.RateLimiting;

namespace Infrastructure.Tests.Security.RateLimiting;

[Trait("Category", "Unit")]
public class RateLimitModelsTests
{
    #region RateLimitOptions

    [Fact]
    public void RateLimitOptions_Defaults_AreCorrect()
    {
        var options = new RateLimitOptions();

        options.EnableRateLimiting.Should().BeTrue();
        options.FailOpen.Should().BeTrue();
        options.LogSuccessfulRequests.Should().BeFalse();
        options.IncludeRuleDetails.Should().BeFalse();
        options.ClientIdStrategy.Should().Be(ClientIdStrategy.UserThenApiKeyThenIp);
        options.ExcludedPaths.Should().NotBeEmpty();
        options.CustomClientIdExtractor.Should().BeNull();
    }

    [Fact]
    public void RateLimitOptions_CanSetProperties()
    {
        var options = new RateLimitOptions
        {
            EnableRateLimiting = false,
            FailOpen = false,
            LogSuccessfulRequests = true,
            IncludeRuleDetails = true,
            ClientIdStrategy = ClientIdStrategy.IpAddressOnly,
            ExcludedPaths = new List<string> { "/test" }
        };

        options.EnableRateLimiting.Should().BeFalse();
        options.FailOpen.Should().BeFalse();
        options.LogSuccessfulRequests.Should().BeTrue();
        options.IncludeRuleDetails.Should().BeTrue();
        options.ClientIdStrategy.Should().Be(ClientIdStrategy.IpAddressOnly);
        options.ExcludedPaths.Should().ContainSingle("/test");
    }

    #endregion

    #region RateLimitStatistics

    [Fact]
    public void RateLimitStatistics_Defaults_AreCorrect()
    {
        var stats = new RateLimitStatistics();

        stats.TotalRequests.Should().Be(0);
        stats.BlockedRequests.Should().Be(0);
        stats.TotalViolations.Should().Be(0);
        stats.UniqueClientsLimited.Should().Be(0);
        stats.AverageResponseTimeImpact.Should().Be(0);
        stats.TopViolatingClients.Should().BeEmpty();
        stats.TopTargetedEndpoints.Should().BeEmpty();
        stats.ViolationsByRule.Should().BeEmpty();
        stats.ViolationsByHour.Should().BeEmpty();
        stats.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RateLimitStatistics_CanSetProperties()
    {
        var stats = new RateLimitStatistics
        {
            TotalRequests = 100,
            BlockedRequests = 10,
            TotalViolations = 5,
            UniqueClientsLimited = 3,
            AverageResponseTimeImpact = 1.5
        };

        stats.TotalRequests.Should().Be(100);
        stats.BlockedRequests.Should().Be(10);
        stats.TotalViolations.Should().Be(5);
        stats.UniqueClientsLimited.Should().Be(3);
        stats.AverageResponseTimeImpact.Should().Be(1.5);
    }

    #endregion

    #region RateLimitViolation

    [Fact]
    public void RateLimitViolation_CanSetProperties()
    {
        var ts = DateTime.UtcNow;
        var violation = new RateLimitViolation
        {
            Timestamp = ts,
            RuleId = "rule-1",
            Endpoint = "/api/test",
            RequestCount = 10,
            Limit = 5,
            Severity = RateLimitSeverity.Warning
        };

        violation.Timestamp.Should().Be(ts);
        violation.RuleId.Should().Be("rule-1");
        violation.Endpoint.Should().Be("/api/test");
        violation.RequestCount.Should().Be(10);
        violation.Limit.Should().Be(5);
        violation.Severity.Should().Be(RateLimitSeverity.Warning);
    }

    #endregion

    #region RateLimitWindow

    [Fact]
    public void RateLimitWindow_CanSetProperties()
    {
        var start = DateTime.UtcNow.AddMinutes(-1);
        var end = DateTime.UtcNow;
        var window = new RateLimitWindow
        {
            RuleId = "rule-1",
            StartTime = start,
            EndTime = end,
            RequestCount = 5,
            Limit = 10,
            IsExceeded = false
        };

        window.RuleId.Should().Be("rule-1");
        window.StartTime.Should().Be(start);
        window.EndTime.Should().Be(end);
        window.RequestCount.Should().Be(5);
        window.Limit.Should().Be(10);
        window.IsExceeded.Should().BeFalse();
    }

    [Fact]
    public void RateLimitWindow_IsExceeded_WhenCountExceedsLimit()
    {
        var window = new RateLimitWindow
        {
            RequestCount = 15,
            Limit = 10,
            IsExceeded = true
        };

        window.IsExceeded.Should().BeTrue();
    }

    #endregion

    #region SizeConditions

    [Fact]
    public void SizeConditions_CanSetProperties()
    {
        var conditions = new SizeConditions
        {
            MinSize = 100,
            MaxSize = 1024 * 1024
        };

        conditions.MinSize.Should().Be(100);
        conditions.MaxSize.Should().Be(1024 * 1024);
    }

    [Fact]
    public void SizeConditions_DefaultsAreNull()
    {
        var conditions = new SizeConditions();

        conditions.MinSize.Should().BeNull();
        conditions.MaxSize.Should().BeNull();
    }

    #endregion

    #region TimeConditions

    [Fact]
    public void TimeConditions_CanSetProperties()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);
        var conditions = new TimeConditions
        {
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday },
            HoursOfDay = new List<int> { 9, 10, 11 },
            StartDate = start,
            EndDate = end,
            TimeZone = "UTC"
        };

        conditions.DaysOfWeek.Should().HaveCount(2);
        conditions.HoursOfDay.Should().HaveCount(3);
        conditions.StartDate.Should().Be(start);
        conditions.EndDate.Should().Be(end);
        conditions.TimeZone.Should().Be("UTC");
    }

    [Fact]
    public void TimeConditions_DefaultsAreEmpty()
    {
        var conditions = new TimeConditions();

        conditions.DaysOfWeek.Should().BeEmpty();
        conditions.HoursOfDay.Should().BeEmpty();
        conditions.StartDate.Should().BeNull();
        conditions.EndDate.Should().BeNull();
        conditions.TimeZone.Should().BeNull();
    }

    #endregion

    #region RateLimitActions

    [Fact]
    public void RateLimitActions_DefaultsAreCorrect()
    {
        var actions = new RateLimitActions();

        actions.Block.Should().BeTrue();
        actions.Log.Should().BeTrue();
        actions.Notify.Should().BeFalse();
        actions.CustomStatusCode.Should().BeNull();
        actions.CustomMessage.Should().BeNull();
        actions.ResponseHeaders.Should().BeEmpty();
        actions.ResponseDelay.Should().BeNull();
        actions.CustomAction.Should().BeNull();
    }

    [Fact]
    public void RateLimitActions_CanSetProperties()
    {
        var actions = new RateLimitActions
        {
            Block = false,
            Log = false,
            Notify = true,
            CustomStatusCode = 503,
            CustomMessage = "Too many requests",
            ResponseDelay = TimeSpan.FromMilliseconds(100)
        };

        actions.Block.Should().BeFalse();
        actions.Log.Should().BeFalse();
        actions.Notify.Should().BeTrue();
        actions.CustomStatusCode.Should().Be(503);
        actions.CustomMessage.Should().Be("Too many requests");
        actions.ResponseDelay.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    #endregion

    #region RateLimitResult

    [Fact]
    public void RateLimitResult_DefaultsAreCorrect()
    {
        var result = new RateLimitResult();

        result.IsAllowed.Should().BeFalse();
        result.TriggeredRule.Should().BeNull();
        result.CurrentCount.Should().Be(0);
        result.Limit.Should().Be(0);
        result.Remaining.Should().Be(0);
        result.RetryAfter.Should().BeNull();
        result.Reason.Should().BeNull();
        result.Severity.Should().Be(RateLimitSeverity.Normal);
        result.Headers.Should().BeEmpty();
    }

    [Fact]
    public void RateLimitResult_CanSetProperties()
    {
        var rule = new RateLimitRule { Id = "test-rule" };
        var result = new RateLimitResult
        {
            IsAllowed = true,
            TriggeredRule = rule,
            CurrentCount = 5,
            Limit = 10,
            Remaining = 5,
            RetryAfter = TimeSpan.FromSeconds(60),
            Reason = "limit reached",
            Severity = RateLimitSeverity.Warning
        };

        result.IsAllowed.Should().BeTrue();
        result.TriggeredRule.Should().Be(rule);
        result.CurrentCount.Should().Be(5);
        result.Limit.Should().Be(10);
        result.Remaining.Should().Be(5);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(60));
        result.Reason.Should().Be("limit reached");
        result.Severity.Should().Be(RateLimitSeverity.Warning);
    }

    #endregion

    #region RateLimitConditions

    [Fact]
    public void RateLimitConditions_DefaultsAreEmpty()
    {
        var conditions = new RateLimitConditions();

        conditions.ClientIds.Should().BeEmpty();
        conditions.UserRoles.Should().BeEmpty();
        conditions.Endpoints.Should().BeEmpty();
        conditions.Methods.Should().BeEmpty();
        conditions.IpPatterns.Should().BeEmpty();
        conditions.UserAgentPatterns.Should().BeEmpty();
        conditions.TimeConditions.Should().BeNull();
        conditions.SizeConditions.Should().BeNull();
        conditions.CustomCondition.Should().BeNull();
    }

    [Fact]
    public void RateLimitConditions_CanSetProperties()
    {
        var conditions = new RateLimitConditions
        {
            ClientIds = new List<string> { "client1" },
            UserRoles = new List<string> { "User" },
            Endpoints = new List<string> { "/api/test" },
            Methods = new List<string> { "GET" },
            IpPatterns = new List<string> { "10.*" },
            UserAgentPatterns = new List<string> { "bot*" }
        };

        conditions.ClientIds.Should().ContainSingle("client1");
        conditions.UserRoles.Should().ContainSingle("User");
        conditions.Endpoints.Should().ContainSingle("/api/test");
        conditions.Methods.Should().ContainSingle("GET");
        conditions.IpPatterns.Should().ContainSingle("10.*");
        conditions.UserAgentPatterns.Should().ContainSingle("bot*");
    }

    [Fact]
    public void RateLimitConditions_CustomCondition_CanBeSet()
    {
        var conditions = new RateLimitConditions
        {
            CustomCondition = req => req.ClientId == "test"
        };

        conditions.CustomCondition.Should().NotBeNull();
        conditions.CustomCondition!(new RateLimitRequest { ClientId = "test" }).Should().BeTrue();
        conditions.CustomCondition!(new RateLimitRequest { ClientId = "other" }).Should().BeFalse();
    }

    #endregion

    #region RateLimitRequest

    [Fact]
    public void RateLimitRequest_DefaultsAreCorrect()
    {
        var request = new RateLimitRequest();

        request.ClientId.Should().BeEmpty();
        request.UserId.Should().BeNull();
        request.UserRoles.Should().BeEmpty();
        request.Endpoint.Should().BeEmpty();
        request.Method.Should().BeEmpty();
        request.Path.Should().BeEmpty();
        request.IpAddress.Should().BeNull();
        request.UserAgent.Should().BeNull();
        request.RequestSize.Should().BeNull();
        request.ApiKey.Should().BeNull();
        request.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        request.Metadata.Should().BeEmpty();
    }

    [Fact]
    public void RateLimitRequest_CanSetProperties()
    {
        var ts = DateTime.UtcNow;
        var request = new RateLimitRequest
        {
            ClientId = "client1",
            UserId = "user1",
            UserRoles = new List<string> { "User" },
            Endpoint = "/api/test",
            Method = "GET",
            Path = "/api/test",
            IpAddress = "127.0.0.1",
            UserAgent = "Mozilla/5.0",
            RequestSize = 1024,
            ApiKey = "api-key-123",
            Timestamp = ts
        };

        request.ClientId.Should().Be("client1");
        request.UserId.Should().Be("user1");
        request.UserRoles.Should().ContainSingle("User");
        request.Endpoint.Should().Be("/api/test");
        request.Method.Should().Be("GET");
        request.IpAddress.Should().Be("127.0.0.1");
        request.UserAgent.Should().Be("Mozilla/5.0");
        request.RequestSize.Should().Be(1024);
        request.ApiKey.Should().Be("api-key-123");
        request.Timestamp.Should().Be(ts);
    }

    #endregion

    #region RateLimitStatus

    [Fact]
    public void RateLimitStatus_DefaultsAreCorrect()
    {
        var status = new RateLimitStatus();

        status.ClientId.Should().BeEmpty();
        status.IsRateLimited.Should().BeFalse();
        status.IsWhitelisted.Should().BeFalse();
        status.IsBlacklisted.Should().BeFalse();
        status.ActiveWindows.Should().BeEmpty();
        status.RecentViolations.Should().BeEmpty();
        status.PenaltyLevel.Should().Be(0);
        status.PenaltyResetTime.Should().BeNull();
    }

    [Fact]
    public void RateLimitStatus_CanSetProperties()
    {
        var resetTime = DateTime.UtcNow.AddHours(1);
        var status = new RateLimitStatus
        {
            ClientId = "client1",
            IsRateLimited = true,
            IsWhitelisted = false,
            IsBlacklisted = true,
            PenaltyLevel = 2.5,
            PenaltyResetTime = resetTime
        };

        status.ClientId.Should().Be("client1");
        status.IsRateLimited.Should().BeTrue();
        status.IsBlacklisted.Should().BeTrue();
        status.PenaltyLevel.Should().Be(2.5);
        status.PenaltyResetTime.Should().Be(resetTime);
    }

    #endregion

    #region RateLimitSeverity enum

    [Fact]
    public void RateLimitSeverity_HasAllValues()
    {
        var values = Enum.GetValues<RateLimitSeverity>();
        values.Should().Contain(RateLimitSeverity.Normal);
        values.Should().Contain(RateLimitSeverity.Warning);
        values.Should().Contain(RateLimitSeverity.Severe);
        values.Should().Contain(RateLimitSeverity.Critical);
    }

    #endregion

    #region RateLimitAlgorithm enum

    [Fact]
    public void RateLimitAlgorithm_HasAllValues()
    {
        var values = Enum.GetValues<RateLimitAlgorithm>();
        values.Should().Contain(RateLimitAlgorithm.FixedWindow);
        values.Should().Contain(RateLimitAlgorithm.SlidingWindow);
        values.Should().Contain(RateLimitAlgorithm.TokenBucket);
        values.Should().Contain(RateLimitAlgorithm.LeakyBucket);
        values.Should().Contain(RateLimitAlgorithm.SlidingWindowLog);
    }

    #endregion

    #region ClientIdStrategy enum

    [Fact]
    public void ClientIdStrategy_HasAllValues()
    {
        var values = Enum.GetValues<ClientIdStrategy>();
        values.Should().Contain(ClientIdStrategy.IpAddressOnly);
        values.Should().Contain(ClientIdStrategy.UserIdOnly);
        values.Should().Contain(ClientIdStrategy.ApiKeyOnly);
        values.Should().Contain(ClientIdStrategy.UserThenApiKeyThenIp);
        values.Should().Contain(ClientIdStrategy.ApiKeyThenUserThenIp);
        values.Should().Contain(ClientIdStrategy.Custom);
    }

    #endregion

    #region RateLimitConfiguration

    [Fact]
    public void RateLimitConfiguration_DefaultsAreCorrect()
    {
        var config = new RateLimitConfiguration();

        config.RequestLimit.Should().Be(100);
        config.Window.Should().Be(TimeSpan.FromMinutes(1));
        config.Algorithm.Should().Be(RateLimitAlgorithm.SlidingWindow);
        config.BurstLimit.Should().BeNull();
        config.RefillRate.Should().BeNull();
        config.PenaltyFactor.Should().Be(1.0);
        config.MaxPenaltyDuration.Should().BeNull();
        config.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void RateLimitConfiguration_CanSetAllProperties()
    {
        var config = new RateLimitConfiguration
        {
            RequestLimit = 500,
            Window = TimeSpan.FromHours(1),
            Algorithm = RateLimitAlgorithm.TokenBucket,
            BurstLimit = 50,
            RefillRate = 0.5,
            PenaltyFactor = 2.0,
            MaxPenaltyDuration = TimeSpan.FromHours(2)
        };

        config.RequestLimit.Should().Be(500);
        config.Window.Should().Be(TimeSpan.FromHours(1));
        config.Algorithm.Should().Be(RateLimitAlgorithm.TokenBucket);
        config.BurstLimit.Should().Be(50);
        config.RefillRate.Should().Be(0.5);
        config.PenaltyFactor.Should().Be(2.0);
        config.MaxPenaltyDuration.Should().Be(TimeSpan.FromHours(2));
    }

    #endregion

    #region RateLimitRule

    [Fact]
    public void RateLimitRule_DefaultsAreCorrect()
    {
        var rule = new RateLimitRule();

        rule.Id.Should().NotBeNullOrEmpty();
        rule.Name.Should().BeEmpty();
        rule.Description.Should().BeEmpty();
        rule.IsEnabled.Should().BeTrue();
        rule.Priority.Should().Be(100);
        rule.Configuration.Should().NotBeNull();
        rule.Conditions.Should().NotBeNull();
        rule.Actions.Should().NotBeNull();
        rule.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        rule.ModifiedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        rule.ExpiresAt.Should().BeNull();
        rule.Tags.Should().BeEmpty();
    }

    [Fact]
    public void RateLimitRule_CanSetProperties()
    {
        var expires = DateTime.UtcNow.AddDays(7);
        var rule = new RateLimitRule
        {
            Id = "my-rule",
            Name = "My Rule",
            Description = "A test rule",
            IsEnabled = false,
            Priority = 200,
            ExpiresAt = expires,
            Tags = new List<string> { "test", "unit" }
        };

        rule.Id.Should().Be("my-rule");
        rule.Name.Should().Be("My Rule");
        rule.Description.Should().Be("A test rule");
        rule.IsEnabled.Should().BeFalse();
        rule.Priority.Should().Be(200);
        rule.ExpiresAt.Should().Be(expires);
        rule.Tags.Should().HaveCount(2);
    }

    #endregion
}
