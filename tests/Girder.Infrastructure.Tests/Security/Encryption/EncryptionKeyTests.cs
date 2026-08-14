using Girder.Infrastructure.Security.Encryption;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class EncryptionKeyTests
{
    #region IsValid

    [Fact]
    public void IsValid_ActiveKeyWithMaterial_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active, hasExpiry: false);

        key.IsValid().Should().BeTrue();
    }

    [Fact]
    public void IsValid_DisabledKey_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Disabled);

        key.IsValid().Should().BeFalse();
    }

    [Fact]
    public void IsValid_ExpiredKey_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.ExpiresAt = DateTime.UtcNow.AddHours(-1);

        key.IsValid().Should().BeFalse();
    }

    [Fact]
    public void IsValid_EmptyKeyMaterial_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.KeyMaterial = Array.Empty<byte>();

        key.IsValid().Should().BeFalse();
    }

    [Fact]
    public void IsValid_FutureExpiry_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active);
        key.ExpiresAt = DateTime.UtcNow.AddYears(1);

        key.IsValid().Should().BeTrue();
    }

    [Fact]
    public void IsValid_NullExpiry_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active, hasExpiry: false);

        key.IsValid().Should().BeTrue();
    }

    #endregion

    #region CanBeUsedFor

    [Fact]
    public void CanBeUsedFor_MatchingPurpose_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active);
        key.Purpose = KeyPurpose.DataEncryption;

        key.CanBeUsedFor(KeyPurpose.DataEncryption).Should().BeTrue();
    }

    [Fact]
    public void CanBeUsedFor_DataEncryptionKey_CanBeUsedForAnyPurpose()
    {
        var key = CreateKey(KeyStatus.Active);
        key.Purpose = KeyPurpose.DataEncryption;

        key.CanBeUsedFor(KeyPurpose.Signing).Should().BeTrue();
        key.CanBeUsedFor(KeyPurpose.Authentication).Should().BeTrue();
    }

    [Fact]
    public void CanBeUsedFor_DifferentPurpose_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.Purpose = KeyPurpose.Signing;

        key.CanBeUsedFor(KeyPurpose.Authentication).Should().BeFalse();
    }

    [Fact]
    public void CanBeUsedFor_InvalidKey_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Disabled);
        key.Purpose = KeyPurpose.DataEncryption;

        key.CanBeUsedFor(KeyPurpose.DataEncryption).Should().BeFalse();
    }

    #endregion

    #region IsUsageAllowed

    [Fact]
    public void IsUsageAllowed_NoRestrictions_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = null;

        key.IsUsageAllowed().Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_InvalidKey_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Disabled);

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_MaxEncryptionExceeded_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions { MaxEncryptionOperations = 100 };
        key.UsageStatistics.EncryptionOperations = 100;

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_MaxDataSizeExceeded_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions { MaxDataSize = 1000 };
        key.UsageStatistics.TotalDataEncrypted = 1000;

        key.IsUsageAllowed().Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_IpNotInAllowedList_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedIpRanges = new List<string> { "10.0.0.1" }
        };

        key.IsUsageAllowed(ipAddress: "192.168.1.1").Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_IpInAllowedList_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedIpRanges = new List<string> { "10.0.0.1" }
        };

        key.IsUsageAllowed(ipAddress: "10.0.0.1").Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_WildcardIp_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedIpRanges = new List<string> { "*" }
        };

        key.IsUsageAllowed(ipAddress: "any.ip.address").Should().BeTrue();
    }

    [Fact]
    public void IsUsageAllowed_RoleNotInAllowedList_ReturnsFalse()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedRoles = new List<string> { "Admin" }
        };

        key.IsUsageAllowed(role: "User").Should().BeFalse();
    }

    [Fact]
    public void IsUsageAllowed_RoleInAllowedList_ReturnsTrue()
    {
        var key = CreateKey(KeyStatus.Active);
        key.UsageRestrictions = new KeyUsageRestrictions
        {
            AllowedRoles = new List<string> { "Admin" }
        };

        key.IsUsageAllowed(role: "Admin").Should().BeTrue();
    }

    #endregion

    #region UpdateUsageStatistics

    [Fact]
    public void UpdateUsageStatistics_Encryption_IncrementsEncryptionOps()
    {
        var key = CreateKey(KeyStatus.Active);
        var initialCount = key.UsageStatistics.EncryptionOperations;

        key.UpdateUsageStatistics(1024, isEncryption: true);

        key.UsageStatistics.EncryptionOperations.Should().Be(initialCount + 1);
        key.UsageStatistics.TotalDataEncrypted.Should().Be(1024);
        key.UsageStatistics.LastUsed.Should().NotBeNull();
    }

    [Fact]
    public void UpdateUsageStatistics_Decryption_IncrementsDecryptionOps()
    {
        var key = CreateKey(KeyStatus.Active);

        key.UpdateUsageStatistics(512, isEncryption: false);

        key.UsageStatistics.DecryptionOperations.Should().Be(1);
        key.UsageStatistics.TotalDataDecrypted.Should().Be(512);
    }

    [Fact]
    public void UpdateUsageStatistics_UpdatesDailyStatistics()
    {
        var key = CreateKey(KeyStatus.Active);

        key.UpdateUsageStatistics(100, isEncryption: true);

        var today = DateTime.UtcNow.Date;
        key.UsageStatistics.DailyUsage.Should().ContainKey(today);
        key.UsageStatistics.DailyUsage[today].EncryptionOperations.Should().Be(1);
    }

    [Fact]
    public void UpdateUsageStatistics_MultipleCalls_Accumulates()
    {
        var key = CreateKey(KeyStatus.Active);

        key.UpdateUsageStatistics(100, isEncryption: true);
        key.UpdateUsageStatistics(200, isEncryption: true);
        key.UpdateUsageStatistics(300, isEncryption: false);

        key.UsageStatistics.EncryptionOperations.Should().Be(2);
        key.UsageStatistics.DecryptionOperations.Should().Be(1);
        key.UsageStatistics.TotalDataEncrypted.Should().Be(300);
        key.UsageStatistics.TotalDataDecrypted.Should().Be(300);
    }

    #endregion

    private static EncryptionKey CreateKey(KeyStatus status, bool hasExpiry = true)
    {
        return new EncryptionKey
        {
            Id = Guid.NewGuid().ToString(),
            Status = status,
            KeyMaterial = new byte[32],
            KeySize = 256,
            KeyType = KeyType.Symmetric,
            Purpose = KeyPurpose.DataEncryption,
            ExpiresAt = hasExpiry ? DateTime.UtcNow.AddYears(1) : null,
            UsageStatistics = new KeyUsageStatistics()
        };
    }
}
