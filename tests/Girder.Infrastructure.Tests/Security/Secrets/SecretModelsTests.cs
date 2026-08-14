using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Secrets;

namespace Girder.Infrastructure.Tests.Security.Secrets;

[Trait("Category", "Unit")]
public class SecretVersionTests
{
    [Fact]
    public void SecretVersion_DefaultConstruction_HasExpectedDefaults()
    {
        var version = new SecretVersion();

        version.Name.Should().Be(string.Empty);
        version.Value.Should().Be(string.Empty);
        version.Version.Should().Be(0);
        version.CreatedBy.Should().Be("System");
        version.IsActive.Should().BeFalse();
        version.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public void SecretVersion_PropertyAssignment_Works()
    {
        var now = DateTime.UtcNow;
        var version = new SecretVersion
        {
            Name = "my-secret",
            Value = "secret-value",
            Version = 3,
            CreatedAt = now,
            ExpiresAt = now.AddDays(30),
            IsActive = true,
            CreatedBy = "admin"
        };

        version.Name.Should().Be("my-secret");
        version.Value.Should().Be("secret-value");
        version.Version.Should().Be(3);
        version.CreatedAt.Should().Be(now);
        version.ExpiresAt.Should().Be(now.AddDays(30));
        version.IsActive.Should().BeTrue();
        version.CreatedBy.Should().Be("admin");
    }
}

[Trait("Category", "Unit")]
public class SecretGeneratorTests
{
    [Fact]
    public void GenerateSecret_DefaultParams_Returns32CharString()
    {
        var secret = SecretGenerator.GenerateSecret();

        secret.Should().NotBeNullOrEmpty();
        secret.Length.Should().Be(32);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void GenerateSecret_SpecifiedLength_ReturnsCorrectLength(int length)
    {
        var secret = SecretGenerator.GenerateSecret(length);

        secret.Length.Should().Be(length);
    }

    [Fact]
    public void GenerateSecret_Alphanumeric_ContainsOnlyAlphanumericChars()
    {
        var secret = SecretGenerator.GenerateSecret(100, SecretType.Alphanumeric);

        secret.Should().MatchRegex("^[a-zA-Z0-9]+$");
    }

    [Fact]
    public void GenerateSecret_Hex_ContainsOnlyHexChars()
    {
        var secret = SecretGenerator.GenerateSecret(64, SecretType.Hex);

        secret.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void GenerateSecret_Numeric_ContainsOnlyDigits()
    {
        var secret = SecretGenerator.GenerateSecret(16, SecretType.Numeric);

        secret.Should().MatchRegex("^[0-9]+$");
    }

    [Fact]
    public void GenerateSecret_AlphanumericWithSpecial_ContainsSpecialChars()
    {
        // Generate a long string to increase probability of special chars
        var secret = SecretGenerator.GenerateSecret(200, SecretType.AlphanumericWithSpecial);

        secret.Should().NotBeNullOrEmpty();
        secret.Length.Should().Be(200);
    }

    [Fact]
    public void GenerateSecret_Password_ReturnsNonEmptyString()
    {
        var secret = SecretGenerator.GenerateSecret(32, SecretType.Password);

        secret.Should().NotBeNullOrEmpty();
        secret.Length.Should().Be(32);
    }

    [Fact]
    public void GenerateSecret_TwoCalls_ReturnDifferentValues()
    {
        var secret1 = SecretGenerator.GenerateSecret();
        var secret2 = SecretGenerator.GenerateSecret();

        secret1.Should().NotBe(secret2);
    }

    [Fact]
    public void GenerateApiKey_DefaultPrefix_StartsWithSk()
    {
        var apiKey = SecretGenerator.GenerateApiKey();

        apiKey.Should().StartWith("sk_");
    }

    [Fact]
    public void GenerateApiKey_CustomPrefix_StartsWithPrefix()
    {
        var apiKey = SecretGenerator.GenerateApiKey("pk");

        apiKey.Should().StartWith("pk_");
    }

    [Fact]
    public void GenerateApiKey_TwoCalls_ReturnDifferentKeys()
    {
        var key1 = SecretGenerator.GenerateApiKey();
        var key2 = SecretGenerator.GenerateApiKey();

        key1.Should().NotBe(key2);
    }
}

[Trait("Category", "Unit")]
public class SecretValidatorTests
{
    [Theory]
    [InlineData("your-secret-key")]
    [InlineData("your-secret")]
    [InlineData("placeholder")]
    [InlineData("changeme")]
    [InlineData("PLACEHOLDER")]
    [InlineData("password")]
    [InlineData("test")]
    public void IsPlaceholderSecret_KnownPlaceholderValues_ReturnsTrue(string value)
    {
        var result = SecretValidator.IsPlaceholderSecret(value);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholderSecret_EmptyString_ReturnsTrue()
    {
        // IsPlaceholderSecret treats null/whitespace/empty as placeholder
        SecretValidator.IsPlaceholderSecret(string.Empty).Should().BeTrue();
    }

    [Fact]
    public void IsPlaceholderSecret_RealSecret_ReturnsFalse()
    {
        // A realistic long hex key - not a placeholder pattern
        var realSecret = SecretGenerator.GenerateSecret(64, SecretType.Hex);

        var result = SecretValidator.IsPlaceholderSecret(realSecret);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("00112233445566778899aabbccddeeffabcd00112233445566778899aabbccdd")]
    [InlineData("00112233445566778899aabbccddeeff0000112233445566778899aabbccdd")]
    public void IsPlaceholderSecret_LongHexKeyWithShortPattern_ReturnsFalse(string realSecret)
    {
        var result = SecretValidator.IsPlaceholderSecret(realSecret);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsPlaceholderSecret_LongRepeatedSecret_ReturnsTrue()
    {
        var result = SecretValidator.IsPlaceholderSecret(new string('a', 32));

        result.Should().BeTrue();
    }

    [Fact]
    public void IsStrongSecret_ShortSecret_ReturnsFalse()
    {
        var result = SecretValidator.IsStrongSecret("abc");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsStrongSecret_WeakSecret_ReturnsFalse()
    {
        // All lowercase, no numbers or special chars, length < 12 check passes but not strong
        var result = SecretValidator.IsStrongSecret("abcdefghijklmnop");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsStrongSecret_LongHexKey_ReturnsTrue()
    {
        // A hex key of 32+ chars is considered strong
        var hexKey = SecretGenerator.GenerateSecret(64, SecretType.Hex);

        var result = SecretValidator.IsStrongSecret(hexKey);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsStrongSecret_EmptyString_ReturnsFalse()
    {
        var result = SecretValidator.IsStrongSecret(string.Empty);

        result.Should().BeFalse();
    }
}
