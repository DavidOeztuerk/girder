using Girder.Infrastructure.Security.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.RateLimiting;

[Trait("Category", "Unit")]
public class RateLimitExtensionsTests
{
    #region RateLimitRuleBuilder

    [Fact]
    public void RuleBuilder_AddGlobalRule_AddsRuleWithGlobalPrefix()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddGlobalRule("api", 500, TimeSpan.FromMinutes(1));

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Id.Should().StartWith("global-");
        builder.Rules[0].Configuration.RequestLimit.Should().Be(500);
        builder.Rules[0].Configuration.Algorithm.Should().Be(RateLimitAlgorithm.SlidingWindow);
        builder.Rules[0].Priority.Should().Be(50);
    }

    [Fact]
    public void RuleBuilder_AddRoleRule_AddsRuleWithRoleConditions()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddRoleRule("premium", new[] { "Premium", "Admin" }, 1000, TimeSpan.FromMinutes(1));

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Id.Should().StartWith("role-");
        builder.Rules[0].Conditions.UserRoles.Should().Contain("Premium").And.Contain("Admin");
        builder.Rules[0].Priority.Should().Be(100);
    }

    [Fact]
    public void RuleBuilder_AddEndpointRule_AddsRuleWithEndpointConditions()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddEndpointRule("upload", new[] { "/api/upload" }, 10, TimeSpan.FromMinutes(1));

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Id.Should().StartWith("endpoint-");
        builder.Rules[0].Conditions.Endpoints.Should().Contain("/api/upload");
        builder.Rules[0].Priority.Should().Be(150);
    }

    [Fact]
    public void RuleBuilder_AddCustomRule_AddsRuleDirectly()
    {
        var builder = new RateLimitRuleBuilder();
        var customRule = new RateLimitRule { Id = "custom-123", Name = "Custom", Priority = 999 };

        builder.AddCustomRule(customRule);

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Id.Should().Be("custom-123");
        builder.Rules[0].Priority.Should().Be(999);
    }

    [Fact]
    public void RuleBuilder_AddTimeBasedRule_AddsRuleWithTimePrefixAndSlidingWindow()
    {
        var builder = new RateLimitRuleBuilder();
        var timeConditions = new TimeConditions { HoursOfDay = new List<int> { 9, 10, 11, 12, 13, 14, 15, 16, 17 } };

        builder.AddTimeBasedRule("business-hours", timeConditions, 200, TimeSpan.FromMinutes(1));

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Id.Should().StartWith("time-");
        builder.Rules[0].Configuration.Algorithm.Should().Be(RateLimitAlgorithm.SlidingWindow);
        builder.Rules[0].Conditions.TimeConditions.Should().NotBeNull();
        builder.Rules[0].Priority.Should().Be(75);
    }

    [Fact]
    public void RuleBuilder_AddBurstProtectionRule_AddsTokenBucketRule()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddBurstProtectionRule("burst", 50, 10.0, TimeSpan.FromSeconds(1));

        builder.Rules.Should().HaveCount(1);
        builder.Rules[0].Id.Should().StartWith("burst-");
        builder.Rules[0].Configuration.Algorithm.Should().Be(RateLimitAlgorithm.TokenBucket);
        builder.Rules[0].Configuration.BurstLimit.Should().Be(50);
        builder.Rules[0].Configuration.RefillRate.Should().Be(10.0);
        builder.Rules[0].Priority.Should().Be(200);
    }

    [Fact]
    public void RuleBuilder_MultipleRules_AllStoredInOrder()
    {
        var builder = new RateLimitRuleBuilder();

        builder
            .AddGlobalRule("global", 1000, TimeSpan.FromMinutes(1))
            .AddRoleRule("admin", new[] { "Admin" }, 5000, TimeSpan.FromMinutes(1))
            .AddEndpointRule("heavy", new[] { "/api/heavy" }, 5, TimeSpan.FromMinutes(1));

        builder.Rules.Should().HaveCount(3);
    }

    [Fact]
    public void RuleBuilder_NameWithSpaces_IdHasHyphens()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddGlobalRule("my rule name", 100, TimeSpan.FromMinutes(1));

        builder.Rules[0].Id.Should().Be("global-my-rule-name");
    }

    [Fact]
    public void RuleBuilder_CustomAlgorithm_PreservedInConfiguration()
    {
        var builder = new RateLimitRuleBuilder();

        builder.AddGlobalRule("fixed-window", 100, TimeSpan.FromMinutes(1), RateLimitAlgorithm.FixedWindow);

        builder.Rules[0].Configuration.Algorithm.Should().Be(RateLimitAlgorithm.FixedWindow);
    }

    #endregion


}
