using Girder.Abstractions.Security;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Keys;
using Girder.Infrastructure.Security.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class JwtServiceTests
{
    private const string TestSecret = "ThisIsATestSecretKeyThatIsAtLeast64CharactersLongForHmacSha256Signing!!";
    private const string TestIssuer = "TestIssuer";
    private const string TestAudience = "TestAudience";

    private readonly ITokenRevocationEvaluator _tokenRevocationService;
    private readonly ITokenRevocationWriter _revocationWriter = Substitute.For<ITokenRevocationWriter>();
    private readonly ILogger<JwtService> _logger;

    public JwtServiceTests()
    {
        _tokenRevocationService = Substitute.For<ITokenRevocationEvaluator>();
        _logger = Substitute.For<ILogger<JwtService>>();
    }

    /// <summary>
    /// Role inheritance is a property of the application's catalogue, not of the
    /// token service, so the tests that assert it declare their own.
    /// </summary>
    private static readonly IPermissionCatalog RoleHierarchy = new PermissionCatalogBuilder()
        .RoleInherits("Moderator", "User")
        .RoleInherits("Admin", "Moderator")
        .RoleInherits("SuperAdmin", "Admin")
        .Build();

    private JwtService CreateService(
        JwtSettings? settings = null,
        IPermissionCatalog? catalog = null)
    {
        var jwtSettings = settings ?? new JwtSettings
        {
            Secret = TestSecret,
            Issuer = TestIssuer,
            Audience = TestAudience,
            ExpireMinutes = 60
        };
        var options = Options.Create(jwtSettings);
        var shared = SigningKey.FromSharedSecret(jwtSettings.Secret, kid: null);
        var keys = new KeyRing([shared], shared);
        return new JwtService(options, keys, _logger, catalog, _tokenRevocationService, _revocationWriter);
    }

    private static UserClaims CreateValidUserClaims() => new()
    {
        UserId = "user-123",
        Email = "test@example.com",
        Roles = ["User"],
        Permissions = [],
        EmailVerified = true,
        AccountStatus = "Active"
    };

    // --- Constructor Validation ---



    [Fact]
    public void Constructor_WithEmptyIssuer_ThrowsInvalidOperationException()
    {
        var settings = new JwtSettings
        {
            Secret = TestSecret,
            Issuer = "",
            Audience = TestAudience,
            ExpireMinutes = 60
        };

        var act = () => CreateService(settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Issuer is required*");
    }

    [Fact]
    public void Constructor_WithEmptyAudience_ThrowsInvalidOperationException()
    {
        var settings = new JwtSettings
        {
            Secret = TestSecret,
            Issuer = TestIssuer,
            Audience = "",
            ExpireMinutes = 60
        };

        var act = () => CreateService(settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Audience is required*");
    }

    [Fact]
    public void Constructor_WithZeroExpireMinutes_ThrowsInvalidOperationException()
    {
        var settings = new JwtSettings
        {
            Secret = TestSecret,
            Issuer = TestIssuer,
            Audience = TestAudience,
            ExpireMinutes = 0
        };

        var act = () => CreateService(settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ExpireMinutes cannot be 0*");
    }

    [Fact]
    public void Constructor_WithValidSettings_DoesNotThrow()
    {
        var act = () => CreateService();

        act.Should().NotThrow();
    }

    // --- GenerateTokenAsync ---

    [Fact]
    public async Task GenerateTokenAsync_WithValidUser_ReturnsTokenResult()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();

        var result = await service.GenerateTokenAsync(user);

        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.TokenType.Should().Be("Bearer");
        result.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task GenerateTokenAsync_TokenContainsExpectedClaims()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == "user-123");
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Email && c.Value == "test@example.com");
        token.Claims.Should().Contain(c => c.Type == "email_verified");
        token.Claims.Should().Contain(c => c.Type == "account_status" && c.Value == "Active");
        token.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Jti);
    }

    [Fact]
    public async Task GenerateTokenAsync_WithSessionId_IncludesSessionClaim()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();
        user.SessionId = "session-abc";

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        token.Claims.Should().Contain(c => c.Type == "session_id" && c.Value == "session-abc");
    }

    [Fact]
    public async Task GenerateTokenAsync_WithCustomClaims_IncludesCustomClaims()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();
        user.CustomClaims = new Dictionary<string, string>
        {
            ["custom_key"] = "custom_value"
        };

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        token.Claims.Should().Contain(c => c.Type == "custom_key" && c.Value == "custom_value");
    }

    [Fact]
    public async Task GenerateTokenAsync_SuperAdminRole_InheritsAllRoles()
    {
        var service = CreateService(catalog: RoleHierarchy);
        var user = CreateValidUserClaims();
        user.Roles = ["SuperAdmin"];

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        var roleClaims = token.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        roleClaims.Should().Contain("SuperAdmin");
        roleClaims.Should().Contain("Admin");
        roleClaims.Should().Contain("Moderator");
        roleClaims.Should().Contain("User");
    }

    [Fact]
    public async Task GenerateTokenAsync_AdminRole_InheritsModeratorAndUser()
    {
        var service = CreateService(catalog: RoleHierarchy);
        var user = CreateValidUserClaims();
        user.Roles = ["Admin"];

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        var roleClaims = token.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        roleClaims.Should().Contain("Admin");
        roleClaims.Should().Contain("Moderator");
        roleClaims.Should().Contain("User");
        roleClaims.Should().NotContain("SuperAdmin");
    }

    [Fact]
    public async Task GenerateTokenAsync_ModeratorRole_InheritsUser()
    {
        var service = CreateService(catalog: RoleHierarchy);
        var user = CreateValidUserClaims();
        user.Roles = ["Moderator"];

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        var roleClaims = token.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        roleClaims.Should().Contain("Moderator");
        roleClaims.Should().Contain("User");
        roleClaims.Should().NotContain("Admin");
    }

    [Fact]
    public async Task GenerateTokenAsync_MergesRolePermissionsWithUserPermissions()
    {
        var catalog = new PermissionCatalogBuilder()
            .Role("User", "profile:view_own")
            .Build();
        var service = CreateService(catalog: catalog);
        var user = CreateValidUserClaims();
        user.Roles = ["User"];
        user.Permissions = ["custom:permission"];

        var result = await service.GenerateTokenAsync(user);

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        var permissions = token.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList();
        permissions.Should().Contain("custom:permission");
        permissions.Should().Contain("profile:view_own", "the catalogue grants it through the User role");
    }

    [Fact]
    public async Task GenerateTokenAsync_WithEmptyUserId_ThrowsArgumentException()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();
        user.UserId = "";

        var act = () => service.GenerateTokenAsync(user);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*UserId is required*");
    }

    [Fact]
    public async Task GenerateTokenAsync_WithInvalidEmail_ThrowsArgumentException()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();
        user.Email = "not-an-email";

        var act = () => service.GenerateTokenAsync(user);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Valid email is required*");
    }

    /// <summary>
    /// A subject needs an identifier and an address, not a name in two parts.
    /// </summary>
    /// <remarks>
    /// Requiring a given and a family name refuses tokens to mononyms, to names
    /// that do not split that way, and to service accounts — and the two fields
    /// were never written into the token, so refusing bought nothing.
    /// </remarks>
    [Fact]
    public async Task GenerateTokenAsync_WithoutAName_Succeeds()
    {
        var service = CreateService();
        var user = new UserClaims
        {
            UserId = "user-without-a-name",
            Email = "someone@example.com"
        };

        var result = await service.GenerateTokenAsync(user);

        result.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // --- GenerateRefreshTokenAsync ---

    [Fact]
    public async Task GenerateRefreshTokenAsync_ReturnsBase64String()
    {
        var service = CreateService();

        var refreshToken = await service.GenerateRefreshTokenAsync();

        refreshToken.Should().NotBeNullOrEmpty();
        var act = () => Convert.FromBase64String(refreshToken);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task GenerateRefreshTokenAsync_ReturnsDifferentTokensEachTime()
    {
        var service = CreateService();

        var token1 = await service.GenerateRefreshTokenAsync();
        var token2 = await service.GenerateRefreshTokenAsync();

        token1.Should().NotBe(token2);
    }

    // --- ValidateTokenAsync ---

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ReturnsPrincipal()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();
        var tokenResult = await service.GenerateTokenAsync(user);

        _tokenRevocationService.EvaluateAsync(Arg.Any<TokenIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<RevocationVerdict>(RevocationVerdict.Valid));

        var principal = await service.ValidateTokenAsync(tokenResult.AccessToken);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("user-123");
    }

    [Fact]
    public async Task ValidateTokenAsync_WithEmptyToken_ReturnsNull()
    {
        var service = CreateService();

        var principal = await service.ValidateTokenAsync("");

        principal.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithNullToken_ReturnsNull()
    {
        var service = CreateService();

        var principal = await service.ValidateTokenAsync(null!);

        principal.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithRevokedToken_ReturnsNull()
    {
        var service = CreateService();
        var user = CreateValidUserClaims();
        var tokenResult = await service.GenerateTokenAsync(user);

        _tokenRevocationService.EvaluateAsync(Arg.Any<TokenIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<RevocationVerdict>(
                new RevocationVerdict(true, RevocationReason.TokenRevoked, false)));

        var principal = await service.ValidateTokenAsync(tokenResult.AccessToken);

        principal.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithExpiredToken_ReturnsNull()
    {
        var settings = new JwtSettings
        {
            Secret = TestSecret,
            Issuer = TestIssuer,
            Audience = TestAudience,
            ExpireMinutes = -1
        };
        var service = CreateService(settings);
        var user = CreateValidUserClaims();
        var tokenResult = await service.GenerateTokenAsync(user);

        var principal = await service.ValidateTokenAsync(tokenResult.AccessToken);

        principal.Should().BeNull();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidToken_ReturnsNull()
    {
        var service = CreateService();

        var principal = await service.ValidateTokenAsync("invalid.token.here");

        principal.Should().BeNull();
    }

    // --- GetPrincipalFromExpiredTokenAsync ---

    [Fact]
    public async Task GetPrincipalFromExpiredTokenAsync_WithExpiredToken_ReturnsPrincipal()
    {
        var settings = new JwtSettings
        {
            Secret = TestSecret,
            Issuer = TestIssuer,
            Audience = TestAudience,
            ExpireMinutes = -1
        };
        var service = CreateService(settings);
        var user = CreateValidUserClaims();
        var tokenResult = await service.GenerateTokenAsync(user);

        var principal = await service.GetPrincipalFromExpiredTokenAsync(tokenResult.AccessToken);

        principal.Should().NotBeNull();
        principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value.Should().Be("user-123");
    }

    [Fact]
    public async Task GetPrincipalFromExpiredTokenAsync_WithInvalidToken_ReturnsNull()
    {
        var service = CreateService();

        var principal = await service.GetPrincipalFromExpiredTokenAsync("totally.invalid.token");

        principal.Should().BeNull();
    }

    // --- RevokeTokenAsync ---

    [Fact]
    public async Task RevokeTokenAsync_CallsTokenRevocationService()
    {
        var service = CreateService();

        await service.RevokeTokenAsync("test-jti", "user-123");

        await _revocationWriter.Received(1)
            .RevokeTokenAsync("test-jti", Arg.Any<DateTimeOffset>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A secret too short to sign with is refused where the key is made, not
    /// where a token is issued.
    /// </summary>
    /// <remarks>
    /// It used to be checked inside <see cref="JwtService"/>, which meant the
    /// service also demanded a secret from deployments that configured a key
    /// pair and had none.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("too-short")]
    public void A_secret_too_short_to_sign_with_is_refused(string secret)
    {
        var build = () => SigningKey.FromSharedSecret(secret, kid: null);

        build.Should().Throw<ArgumentException>();
    }
}
