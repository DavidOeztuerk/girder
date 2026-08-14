using Infrastructure.Security.Encryption;

namespace Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class EncryptionKeyTopUpTests
{
    private static EncryptionKey CreateActiveKey() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Status = KeyStatus.Active,
        KeyMaterial = new byte[32],
        KeySize = 256,
        KeyType = KeyType.Symmetric,
        Purpose = KeyPurpose.DataEncryption,
        UsageStatistics = new KeyUsageStatistics()
    };

    #region CIDR IP matching

    [Fact]
    public void IsUsageAllowed_CidrRange_MatchingPrefix_ReturnsTrue()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedIpRanges = new List<string> { "192.168.1/24" }
        };

        key.IsUsageAllowed(ipAddress: "192.168.1.100").Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_CidrRange_NonMatchingPrefix_ReturnsFalse()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedIpRanges = new List<string> { "10.0.0/24" }
        };

        key.IsUsageAllowed(ipAddress: "192.168.1.100").Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_MultipleRanges_OneMatches_ReturnsTrue()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedIpRanges = new List<string> { "10.0.0.1", "192.168.1.100" }
        };

        key.IsUsageAllowed(ipAddress: "192.168.1.100").Should().BeTrue();
    }

    #endregion

    #region Time-based restrictions

    [Fact]
    public void IsUsageAllowed_ValidFromInFuture_ReturnsFalse()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                ValidFrom = DateTime.UtcNow.AddDays(1)
            }
        };

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_ValidUntilInPast_ReturnsFalse()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                ValidUntil = DateTime.UtcNow.AddDays(-1)
            }
        };

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_AllowedDaysOfWeek_TodayNotIncluded_ReturnsFalse()
    {
        var key = CreateActiveKey();
        // Use a day that's definitely not today
        var today = DateTime.UtcNow.DayOfWeek;
        var notToday = Enum.GetValues<DayOfWeek>()
            .First(d => d != today);

        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                AllowedDaysOfWeek = new List<DayOfWeek> { notToday }
            }
        };

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_AllowedDaysOfWeek_TodayIncluded_ReturnsTrue()
    {
        var key = CreateActiveKey();
        var today = DateTime.UtcNow.DayOfWeek;

        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                AllowedDaysOfWeek = new List<DayOfWeek> { today }
            }
        };

        key.IsUsageAllowed().Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_AllowedHoursOfDay_CurrentHourNotIncluded_ReturnsFalse()
    {
        var key = CreateActiveKey();
        var currentHour = DateTime.UtcNow.Hour;
        // Pick an hour that's definitely not now
        var otherHour = (currentHour + 1) % 24;
        var anotherHour = (currentHour + 2) % 24;

        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                AllowedHoursOfDay = new List<int> { otherHour, anotherHour }
            }
        };

        // If both other hours == current, skip this test
        if (otherHour == currentHour || anotherHour == currentHour)
            return;

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_AllowedHoursOfDay_AllHours_ReturnsTrue()
    {
        var key = CreateActiveKey();
        var allHours = Enumerable.Range(0, 24).ToList();

        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                AllowedHoursOfDay = allHours
            }
        };

        key.IsUsageAllowed().Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_ValidFromInPast_ValidUntilInFuture_ReturnsTrue()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                ValidFrom = DateTime.UtcNow.AddDays(-1),
                ValidUntil = DateTime.UtcNow.AddDays(1)
            }
        };

        key.IsUsageAllowed().Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_EmptyAllowedDays_AnyDayAllowed()
    {
        var key = CreateActiveKey();
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            TimeRestrictions = new TimeBasedRestrictions
            {
                AllowedDaysOfWeek = new List<DayOfWeek>() // empty = no restriction
            }
        };

        key.IsUsageAllowed().Should().BeTrue();
    }

    #endregion

    #region UpdateUsageStatistics old-data cleanup

    [Fact]
    public void UpdateUsageStatistics_OldDailyData_IsCleanedUp()
    {
        var key = CreateActiveKey();
        // Insert old data manually (91 days ago)
        var oldDate = DateTime.UtcNow.AddDays(-91).Date;
        key.UsageStatistics.DailyUsage[oldDate] = new DailyUsageStatistics { EncryptionOperations = 10 };

        // This should clean the old entry
        key.UpdateUsageStatistics(100, isEncryption: true);

        key.UsageStatistics.DailyUsage.Should().NotContainKey(oldDate);
    }

    [Fact]
    public void UpdateUsageStatistics_RecentDailyData_IsKept()
    {
        var key = CreateActiveKey();
        var recentDate = DateTime.UtcNow.AddDays(-30).Date;
        key.UsageStatistics.DailyUsage[recentDate] = new DailyUsageStatistics { EncryptionOperations = 5 };

        key.UpdateUsageStatistics(100, isEncryption: true);

        key.UsageStatistics.DailyUsage.Should().ContainKey(recentDate);
    }

    #endregion

    #region Decryption daily stats

    [Fact]
    public void UpdateUsageStatistics_Decryption_UpdatesDailyDecryptionStats()
    {
        var key = CreateActiveKey();

        key.UpdateUsageStatistics(256, isEncryption: false);

        var today = DateTime.UtcNow.Date;
        key.UsageStatistics.DailyUsage.Should().ContainKey(today);
        key.UsageStatistics.DailyUsage[today].DecryptionOperations.Should().Be(1);
        key.UsageStatistics.DailyUsage[today].DataDecrypted.Should().Be(256);
    }

    #endregion
}
