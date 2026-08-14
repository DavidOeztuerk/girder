using Infrastructure.Security.Monitoring;

namespace Infrastructure.Tests.Security.Monitoring;

[Trait("Category", "Unit")]
public class SecurityAlertModelTests
{
    [Fact]
    public void SecurityAlert_DefaultValues()
    {
        var alert = new SecurityAlert();

        alert.Id.Should().NotBeNullOrEmpty();
        alert.Title.Should().BeEmpty();
        alert.Message.Should().BeEmpty();
        alert.IsRead.Should().BeFalse();
        alert.IsDismissed.Should().BeFalse();
        alert.OccurrenceCount.Should().Be(1);
        alert.Metadata.Should().NotBeNull();
    }

    [Fact]
    public void SecurityAlertStatistics_DefaultValues()
    {
        var stats = new SecurityAlertStatistics();

        stats.TotalAlerts.Should().Be(0);
        stats.CriticalAlerts.Should().Be(0);
        stats.AlertsByType.Should().BeEmpty();
        stats.Timeline.Should().BeEmpty();
    }

    [Fact]
    public void AlertTypeCount_CanSetProperties()
    {
        var count = new AlertTypeCount
        {
            Type = SecurityAlertType.BruteForceAttack,
            Count = 5,
            HighestSeverity = SecurityAlertLevel.Critical
        };

        count.Type.Should().Be(SecurityAlertType.BruteForceAttack);
        count.Count.Should().Be(5);
        count.HighestSeverity.Should().Be(SecurityAlertLevel.Critical);
    }

    [Fact]
    public void AlertTimelinePoint_CanSetProperties()
    {
        var point = new AlertTimelinePoint
        {
            Date = new DateTime(2024, 1, 1),
            Critical = 1,
            High = 2,
            Medium = 3,
            Low = 4,
            Info = 5
        };

        point.Date.Should().Be(new DateTime(2024, 1, 1));
        point.Critical.Should().Be(1);
        point.High.Should().Be(2);
        point.Medium.Should().Be(3);
        point.Low.Should().Be(4);
        point.Info.Should().Be(5);
    }

    [Theory]
    [InlineData(SecurityAlertLevel.Info, 0)]
    [InlineData(SecurityAlertLevel.Low, 1)]
    [InlineData(SecurityAlertLevel.Medium, 2)]
    [InlineData(SecurityAlertLevel.High, 3)]
    [InlineData(SecurityAlertLevel.Critical, 4)]
    public void SecurityAlertLevel_HasCorrectValues(SecurityAlertLevel level, int expected)
    {
        ((int)level).Should().Be(expected);
    }

    [Fact]
    public void SecurityAlertType_HasExpectedValues()
    {
        // Verify a representative set of enum values exist
        Enum.IsDefined(typeof(SecurityAlertType), SecurityAlertType.TokenTheftDetected).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityAlertType), SecurityAlertType.BruteForceAttack).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityAlertType), SecurityAlertType.XSSAttempt).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityAlertType), SecurityAlertType.Custom).Should().BeTrue();
    }

    [Fact]
    public async Task NoOpSecurityAlertNotifier_ReturnsFalse()
    {
        var notifier = new NoOpSecurityAlertNotifier();

        var result = await notifier.NotifyAdminsAsync(new SecurityAlert());

        result.Should().BeFalse();
    }
}
