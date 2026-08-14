using Infrastructure.Security;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class TokenResultTests
{
    [Fact]
    public void TokenResult_DefaultValues()
    {
        var result = new TokenResult();

        result.AccessToken.Should().BeEmpty();
        result.RefreshToken.Should().BeEmpty();
        result.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public void ExpiresIn_ReturnsTotalSeconds()
    {
        var result = new TokenResult
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        result.ExpiresIn.Should().BeCloseTo(1800, 5);
    }

    [Fact]
    public void ExpiresIn_NegativeWhenExpired()
    {
        var result = new TokenResult
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };

        result.ExpiresIn.Should().BeNegative();
    }
}
