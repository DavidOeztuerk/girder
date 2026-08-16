using Girder.Abstractions.Security;
using System.IdentityModel.Tokens.Jwt;
using Girder.Core.Identity;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Identity;

/// <summary>
/// Issues a token and reads it back, so that the claim written by
/// <see cref="JwtService"/> and the claim expected by
/// <see cref="PrincipalFactory"/> cannot drift apart.
/// </summary>
[Trait("Category", "Unit")]
public class TokenRoundTripTests
{
    private const string Secret = "ThisIsATestSecretKeyThatIsAtLeast64CharactersLongForHmacSha256Signing!!";

    private readonly JwtService _jwt;
    private readonly PrincipalFactory _factory = new();

    public TokenRoundTripTests()
    {
        var revocation = Substitute.For<ITokenRevocationEvaluator>();
        revocation.EvaluateAsync(Arg.Any<TokenIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<RevocationVerdict>(RevocationVerdict.Valid));
        var revocationWriter = Substitute.For<ITokenRevocationWriter>();

        _jwt = new JwtService(
            Options.Create(new JwtSettings
            {
                Secret = Secret,
                Issuer = "TestIssuer",
                Audience = "TestAudience",
                ExpireMinutes = 60
            }),
            NullLogger<JwtService>.Instance,
            revocation,
            revocationWriter);
    }

    private static UserClaims ValidClaims(SubjectId subject) => new()
    {
        UserId = subject.ToString(),
        Email = "test@example.com",
        FirstName = "Test",
        LastName = "User",
        Roles = ["User"]
    };

    [Fact]
    public async Task PersonToken_CarriesNoTenantClaim_AndReadsBackAsAPerson()
    {
        var subject = SubjectId.New();
        var claims = ValidClaims(subject);

        var token = await _jwt.GenerateTokenAsync(claims);
        var user = await _jwt.ValidateTokenAsync(token.AccessToken);

        user!.FindFirst(GirderClaimTypes.Tenant).Should().BeNull();

        var principal = _factory.Create(user).Principal;
        principal!.Subject.Should().Be(subject);
        principal.Acting.Should().BeOfType<Capacity.AsSelf>();
    }

    [Fact]
    public async Task CompanyToken_CarriesTheTenantClaim_AndReadsBackAsThatCompany()
    {
        var subject = SubjectId.New();
        var tenant = TenantId.New();
        var claims = ValidClaims(subject);
        claims.Acting = new Capacity.ForCompany(tenant);

        var token = await _jwt.GenerateTokenAsync(claims);
        var user = await _jwt.ValidateTokenAsync(token.AccessToken);

        user!.FindFirst(GirderClaimTypes.Tenant)!.Value.Should().Be(tenant.ToString());

        var principal = _factory.Create(user).Principal;
        principal!.Subject.Should().Be(subject);
        principal.Acting.Should().BeOfType<Capacity.ForCompany>()
            .Which.Tenant.Should().Be(tenant);
    }

    [Fact]
    public async Task SubjectSurvivesTheHandlersClaimMapping()
    {
        var subject = SubjectId.New();

        var token = await _jwt.GenerateTokenAsync(ValidClaims(subject));
        var user = await _jwt.ValidateTokenAsync(token.AccessToken);

        var raw = user!.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                  ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        raw.Should().Be(subject.ToString());
    }
}
