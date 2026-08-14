using Infrastructure.Security;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class UserClaimsTests
{
    [Fact]
    public void UserClaims_DefaultValues()
    {
        var claims = new UserClaims();

        claims.UserId.Should().BeEmpty();
        claims.Email.Should().BeEmpty();
        claims.FirstName.Should().BeEmpty();
        claims.LastName.Should().BeEmpty();
        claims.Roles.Should().BeEmpty();
        claims.Permissions.Should().BeEmpty();
        claims.EmailVerified.Should().BeFalse();
        claims.AccountStatus.Should().Be("Active");
        claims.SessionId.Should().BeNull();
        claims.CustomClaims.Should().BeNull();
    }

    [Fact]
    public void UserClaims_CanSetProperties()
    {
        var claims = new UserClaims
        {
            UserId = "user-1",
            Email = "test@example.com",
            FirstName = "John",
            LastName = "Doe",
            Roles = ["Admin"],
            Permissions = ["admin:manage"],
            EmailVerified = true,
            AccountStatus = "Active",
            SessionId = "session-1",
            CustomClaims = new Dictionary<string, string> { ["key"] = "value" }
        };

        claims.UserId.Should().Be("user-1");
        claims.Email.Should().Be("test@example.com");
        claims.Roles.Should().Contain("Admin");
        claims.CustomClaims!["key"].Should().Be("value");
    }
}
