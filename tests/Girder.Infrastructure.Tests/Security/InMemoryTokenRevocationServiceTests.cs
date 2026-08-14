using Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class InMemoryTokenRevocationServiceTests
{
    private readonly InMemoryTokenRevocationService _service;

    public InMemoryTokenRevocationServiceTests()
    {
        var logger = Substitute.For<ILogger<InMemoryTokenRevocationService>>();
        _service = new InMemoryTokenRevocationService(logger);
    }

    [Fact]
    public async Task RevokeTokenAsync_ByJti_MarksTokenAsRevoked()
    {
        await _service.RevokeTokenAsync("jti-123", TimeSpan.FromMinutes(30));

        var isRevoked = await _service.IsTokenRevokedAsync("jti-123");

        isRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task IsTokenRevokedAsync_NonRevokedToken_ReturnsFalse()
    {
        var isRevoked = await _service.IsTokenRevokedAsync("not-revoked");

        isRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeTokenAsync_WithRequest_StoresRevocationInfo()
    {
        var request = new TokenRevocationRequest
        {
            Jti = "jti-456",
            UserId = "user-1",
            Reason = TokenRevocationReason.SecurityIncident,
            Details = "Suspicious activity"
        };

        await _service.RevokeTokenAsync(request);

        var isRevoked = await _service.IsTokenRevokedAsync("jti-456");
        isRevoked.Should().BeTrue();

        var revokedTokens = (await _service.GetRevokedTokensAsync("user-1")).ToList();
        revokedTokens.Should().HaveCount(1);
        revokedTokens.First().Reason.Should().Be(TokenRevocationReason.SecurityIncident);
    }

    [Fact]
    public async Task RevokeUserTokensAsync_RevokesAllUserTokens()
    {
        var request1 = new TokenRevocationRequest
        {
            Jti = "jti-1",
            UserId = "user-revoke-all",
            Reason = TokenRevocationReason.UserLogout
        };
        var request2 = new TokenRevocationRequest
        {
            Jti = "jti-2",
            UserId = "user-revoke-all",
            Reason = TokenRevocationReason.UserLogout
        };

        await _service.RevokeTokenAsync(request1);
        await _service.RevokeTokenAsync(request2);
        await _service.RevokeUserTokensAsync("user-revoke-all");

        var revokedTokens = (await _service.GetRevokedTokensAsync("user-revoke-all")).ToList();
        revokedTokens.Should().HaveCount(2);
        revokedTokens.Should().AllSatisfy(t => t.Reason.Should().Be(TokenRevocationReason.AdminRevocation));
    }

    [Fact]
    public async Task RevokeUserTokensAsync_NoTokensForUser_DoesNotThrow()
    {
        var act = () => _service.RevokeUserTokensAsync("no-tokens-user");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokeRefreshTokenAsync_MarksRefreshTokenAsRevoked()
    {
        await _service.RevokeRefreshTokenAsync("refresh-abc");

        var isRevoked = await _service.IsRefreshTokenRevokedAsync("refresh-abc");

        isRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task IsRefreshTokenRevokedAsync_NonRevoked_ReturnsFalse()
    {
        var isRevoked = await _service.IsRefreshTokenRevokedAsync("not-revoked-refresh");

        isRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task GetRevokedTokensAsync_NoTokens_ReturnsEmpty()
    {
        var result = await _service.GetRevokedTokensAsync("unknown-user");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRevokedTokensAsync_ReturnsOrderedByRevokedAtDescending()
    {
        var request1 = new TokenRevocationRequest
        {
            Jti = "first",
            UserId = "ordered-user",
            Reason = TokenRevocationReason.UserLogout
        };
        await _service.RevokeTokenAsync(request1);

        var request2 = new TokenRevocationRequest
        {
            Jti = "second",
            UserId = "ordered-user",
            Reason = TokenRevocationReason.UserLogout
        };
        await _service.RevokeTokenAsync(request2);

        var tokens = (await _service.GetRevokedTokensAsync("ordered-user")).ToList();

        tokens.Should().HaveCount(2);
        tokens.First().RevokedAt.Should().BeOnOrAfter(tokens.Last().RevokedAt);
    }

    [Fact]
    public async Task CleanupExpiredTokensAsync_RemovesExpiredTokens()
    {
        // Revoke with immediate past expiry
        var request = new TokenRevocationRequest
        {
            Jti = "expired-jti",
            UserId = "cleanup-user",
            Reason = TokenRevocationReason.UserLogout,
            TokenExpiry = TimeSpan.FromMilliseconds(1)
        };
        await _service.RevokeTokenAsync(request);

        // Wait briefly for expiry
        await Task.Delay(10);

        await _service.CleanupExpiredTokensAsync();

        var isRevoked = await _service.IsTokenRevokedAsync("expired-jti");
        isRevoked.Should().BeFalse();
    }
}
