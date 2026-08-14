using Girder.Infrastructure.Security.Monitoring;

namespace Girder.Infrastructure.Tests.Security.Monitoring;

[Trait("Category", "Unit")]
public class SecurityAlertConfigurationTests
{
    [Fact]
    public void SecurityAlertConfiguration_DefaultValues()
    {
        var config = new SecurityAlertConfiguration();

        config.Enabled.Should().BeTrue();
        config.MaxRecentAlertsInCache.Should().Be(1000);
        config.RetentionDays.Should().Be(60);
        config.EnableEmailNotifications.Should().BeFalse();
        config.NotificationEmails.Should().BeEmpty();
        config.DuplicateAlertThrottleSeconds.Should().Be(300);
        config.EnableAlertAggregation.Should().BeTrue();
        config.Thresholds.Should().NotBeNull();
    }

    [Fact]
    public void AlertThresholds_DefaultValues()
    {
        var thresholds = new AlertThresholds();

        thresholds.AutoBlockIPAfterCriticalAlerts.Should().Be(5);
        thresholds.AutoSuspendUserAfterHighAlerts.Should().Be(10);
        thresholds.MaxAlertsPerIPPerHour.Should().Be(100);
    }
}
