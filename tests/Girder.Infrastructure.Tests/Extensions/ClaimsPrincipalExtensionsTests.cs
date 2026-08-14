using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Infrastructure.Extensions;

namespace Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreatePrincipalWithRole(string role)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Role, role) },
            "TestAuth",
            ClaimTypes.Name,
            ClaimTypes.Role);
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal EmptyPrincipal()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    #region GetUserId

    [Fact]
    public void GetUserId_NoClaims_ReturnsNull()
    {
        var user = EmptyPrincipal();

        user.GetUserId().Should().BeNull();
    }

    [Fact]
    public void GetUserId_SubClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim(JwtRegisteredClaimNames.Sub, "user-123"));

        user.GetUserId().Should().Be("user-123");
    }

    [Fact]
    public void GetUserId_NameIdentifierClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim(ClaimTypes.NameIdentifier, "user-456"));

        user.GetUserId().Should().Be("user-456");
    }

    [Fact]
    public void GetUserId_CustomSubClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("sub", "user-789"));

        user.GetUserId().Should().Be("user-789");
    }

    [Fact]
    public void GetUserId_UserIdClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("user_id", "uid-100"));

        user.GetUserId().Should().Be("uid-100");
    }

    [Fact]
    public void GetUserId_CamelCaseUserIdClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("userId", "uid-200"));

        user.GetUserId().Should().Be("uid-200");
    }

    [Fact]
    public void GetUserId_UidClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("uid", "uid-300"));

        user.GetUserId().Should().Be("uid-300");
    }

    [Fact]
    public void GetUserId_IdClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("id", "uid-400"));

        user.GetUserId().Should().Be("uid-400");
    }

    [Fact]
    public void GetUserId_MultipleClaims_ReturnsFirstInFallbackOrder()
    {
        // JwtRegisteredClaimNames.Sub should win over "user_id"
        var user = CreatePrincipal(
            new Claim("user_id", "should-lose"),
            new Claim(JwtRegisteredClaimNames.Sub, "should-win"));

        user.GetUserId().Should().Be("should-win");
    }

    #endregion

    #region GetRolesSafe

    [Fact]
    public void GetRolesSafe_NoClaims_ReturnsEmpty()
    {
        var user = EmptyPrincipal();

        user.GetRolesSafe().Should().BeEmpty();
    }

    [Fact]
    public void GetRolesSafe_SingleRoleClaim_ReturnsRole()
    {
        var user = CreatePrincipal(new Claim(ClaimTypes.Role, "Admin"));

        user.GetRolesSafe().Should().ContainSingle().Which.Should().Be("Admin");
    }

    [Fact]
    public void GetRolesSafe_MultipleRoleClaims_ReturnsAll()
    {
        var user = CreatePrincipal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim(ClaimTypes.Role, "User"));

        user.GetRolesSafe().Should().BeEquivalentTo("Admin", "User");
    }

    [Fact]
    public void GetRolesSafe_CustomRoleClaim_ReturnsRole()
    {
        var user = CreatePrincipal(new Claim("role", "Moderator"));

        user.GetRolesSafe().Should().ContainSingle().Which.Should().Be("Moderator");
    }

    [Fact]
    public void GetRolesSafe_CommaSeparatedRolesClaim_SplitsCorrectly()
    {
        var user = CreatePrincipal(new Claim("roles", "Admin, User, Moderator"));

        user.GetRolesSafe().Should().BeEquivalentTo("Admin", "User", "Moderator");
    }

    [Fact]
    public void GetRolesSafe_MixedClaimTypes_CombinesAll()
    {
        var user = CreatePrincipal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("role", "Moderator"),
            new Claim("roles", "User,Support"));

        user.GetRolesSafe().Should().BeEquivalentTo("Admin", "Moderator", "User", "Support");
    }

    [Fact]
    public void GetRolesSafe_DuplicateRoles_Deduplicated()
    {
        var user = CreatePrincipal(
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("role", "admin"));

        // Case-insensitive dedup
        user.GetRolesSafe().Should().HaveCount(1);
    }

    [Fact]
    public void GetRolesSafe_EmptyCommaString_SkipsEmpty()
    {
        var user = CreatePrincipal(new Claim("roles", ",,Admin,,"));

        user.GetRolesSafe().Should().ContainSingle().Which.Should().Be("Admin");
    }

    #endregion

    #region GetEmail

    [Fact]
    public void GetEmail_NoClaims_ReturnsNull()
    {
        var user = EmptyPrincipal();

        user.GetEmail().Should().BeNull();
    }

    [Fact]
    public void GetEmail_EmailClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("email", "test@example.com"));

        user.GetEmail().Should().Be("test@example.com");
    }

    [Fact]
    public void GetEmail_ClaimTypesEmailClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim(ClaimTypes.Email, "user@example.com"));

        user.GetEmail().Should().Be("user@example.com");
    }

    [Fact]
    public void GetEmail_BothEmailClaims_PrefersCustomEmail()
    {
        var user = CreatePrincipal(
            new Claim("email", "custom@example.com"),
            new Claim(ClaimTypes.Email, "standard@example.com"));

        user.GetEmail().Should().Be("custom@example.com");
    }

    #endregion

    #region GetUsername

    [Fact]
    public void GetUsername_NoClaims_ReturnsNull()
    {
        var user = EmptyPrincipal();

        user.GetUsername().Should().BeNull();
    }

    [Fact]
    public void GetUsername_UsernameClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("username", "johndoe"));

        user.GetUsername().Should().Be("johndoe");
    }

    [Fact]
    public void GetUsername_PreferredUsernameClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim("preferred_username", "janedoe"));

        user.GetUsername().Should().Be("janedoe");
    }

    [Fact]
    public void GetUsername_ClaimTypesNameClaim_ReturnsValue()
    {
        var user = CreatePrincipal(new Claim(ClaimTypes.Name, "alice"));

        user.GetUsername().Should().Be("alice");
    }

    [Fact]
    public void GetUsername_MultipleClaims_PrefersUsernameFirst()
    {
        var user = CreatePrincipal(
            new Claim(ClaimTypes.Name, "should-lose"),
            new Claim("username", "should-win"));

        user.GetUsername().Should().Be("should-win");
    }

    #endregion

    #region Role Checks (IsSuperAdmin, IsAdmin, IsModerator, IsUser)

    [Fact]
    public void IsSuperAdmin_NoClaims_ReturnsFalse()
    {
        EmptyPrincipal().IsSuperAdmin().Should().BeFalse();
    }

    [Fact]
    public void IsSuperAdmin_WithRole_ReturnsTrue()
    {
        CreatePrincipalWithRole("SuperAdmin").IsSuperAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsSuperAdmin_WithRoleClaim_ReturnsTrue()
    {
        var user = CreatePrincipal(new Claim("role", "SuperAdmin"));

        user.IsSuperAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_WithRole_ReturnsTrue()
    {
        CreatePrincipalWithRole("Admin").IsAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_WithRoleClaim_ReturnsTrue()
    {
        CreatePrincipal(new Claim("role", "Admin")).IsAdmin().Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_WrongRole_ReturnsFalse()
    {
        CreatePrincipalWithRole("User").IsAdmin().Should().BeFalse();
    }

    [Fact]
    public void IsModerator_WithRole_ReturnsTrue()
    {
        CreatePrincipalWithRole("Moderator").IsModerator().Should().BeTrue();
    }

    [Fact]
    public void IsModerator_WithRoleClaim_ReturnsTrue()
    {
        CreatePrincipal(new Claim("role", "Moderator")).IsModerator().Should().BeTrue();
    }

    [Fact]
    public void IsUser_WithRole_ReturnsTrue()
    {
        CreatePrincipalWithRole("User").IsUser().Should().BeTrue();
    }

    [Fact]
    public void IsUser_WithRoleClaim_ReturnsTrue()
    {
        CreatePrincipal(new Claim("role", "User")).IsUser().Should().BeTrue();
    }

    [Fact]
    public void IsUser_NoClaims_ReturnsFalse()
    {
        EmptyPrincipal().IsUser().Should().BeFalse();
    }

    #endregion
}
