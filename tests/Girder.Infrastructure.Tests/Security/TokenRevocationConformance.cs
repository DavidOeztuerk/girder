using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// The behaviour every <see cref="ITokenRevocationService"/> must show,
/// whatever it stores in. Derive one fixture per implementation.
/// </summary>
/// <remarks>
/// A revocation list is a security control, so the contract is stricter than
/// the interface can express: a revocation must take effect on the very next
/// check, and a check that cannot reach its store must not report "not
/// revoked".
/// </remarks>
public abstract class TokenRevocationConformance
{
    protected abstract ITokenRevocationService CreateService();

    private static string NewJti() => $"jti-{Guid.NewGuid():N}";
    private static string NewUserId() => $"user-{Guid.NewGuid():N}";

    [Fact]
    public async Task An_untouched_token_is_not_revoked()
    {
        (await CreateService().IsTokenRevokedAsync(NewJti())).Should().BeFalse();
    }

    [Fact]
    public async Task A_revocation_takes_effect_on_the_next_check()
    {
        var service = CreateService();
        var jti = NewJti();

        await service.RevokeTokenAsync(jti);

        (await service.IsTokenRevokedAsync(jti)).Should().BeTrue();
    }

    [Fact]
    public async Task Revoking_twice_is_harmless()
    {
        var service = CreateService();
        var jti = NewJti();

        await service.RevokeTokenAsync(jti);
        await service.RevokeTokenAsync(jti);

        (await service.IsTokenRevokedAsync(jti)).Should().BeTrue();
    }

    [Fact]
    public async Task Revoking_one_token_leaves_the_others_alone()
    {
        var service = CreateService();
        var revoked = NewJti();
        var untouched = NewJti();

        await service.RevokeTokenAsync(revoked);

        (await service.IsTokenRevokedAsync(untouched)).Should().BeFalse();
    }

    [Fact]
    public async Task A_revoked_refresh_token_is_reported_as_revoked()
    {
        var service = CreateService();
        var refreshToken = $"refresh-{Guid.NewGuid():N}";

        (await service.IsRefreshTokenRevokedAsync(refreshToken)).Should().BeFalse();

        await service.RevokeRefreshTokenAsync(refreshToken);

        (await service.IsRefreshTokenRevokedAsync(refreshToken)).Should().BeTrue();
    }

    [Fact]
    public async Task The_revocation_reason_survives()
    {
        var service = CreateService();
        var userId = NewUserId();
        var jti = NewJti();

        await service.RevokeTokenAsync(new TokenRevocationRequest
        {
            Jti = jti,
            UserId = userId,
            Reason = TokenRevocationReason.SecurityIncident,
            Details = "credential stuffing"
        });

        var revoked = await service.GetRevokedTokensAsync(userId);

        revoked.Should().ContainSingle(t => t.Jti == jti)
            .Which.Reason.Should().Be(TokenRevocationReason.SecurityIncident);
    }
}

[Trait("Category", "Unit")]
public class LegacyInMemoryTokenRevocationConformanceTests : TokenRevocationConformance
{
    protected override ITokenRevocationService CreateService() =>
        new InMemoryTokenRevocationService(NullLogger<InMemoryTokenRevocationService>.Instance);
}
