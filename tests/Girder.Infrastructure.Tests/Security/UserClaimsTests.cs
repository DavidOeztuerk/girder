using Girder.Infrastructure.Security;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class UserClaimsTests
{
    [Fact]
    public void UserClaims_DefaultValues()
    {
        var claims = new UserClaims();

        claims.UserId.Should().BeEmpty();
        claims.Email.Should().BeEmpty();
        claims.Roles.Should().BeEmpty();
        claims.Permissions.Should().BeEmpty();
        // Neither has a default any more, and that is the point: both are read by
        // Girder's own policies, so `false` and "Active" made EmailVerified refuse
        // everyone and ActiveAccount admit everyone — including a suspended
        // account, at the gate meant to stop it. A default does not go missing,
        // it answers.
        claims.EmailVerified.Should().BeNull();
        claims.AccountStatus.Should().BeNull();
        claims.SessionId.Should().BeNull();
        claims.CustomClaims.Should().BeNull();
        claims.CustomClaimArrays.Should().BeNull();
    }

    [Fact]
    public void UserClaims_CanSetProperties()
    {
        var claims = new UserClaims
        {
            UserId = "user-1",
            Email = "test@example.com",
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
