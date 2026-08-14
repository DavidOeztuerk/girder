using System.Security.Claims;

namespace Infrastructure.Security;

public interface IJwtService
{
    Task<TokenResult> GenerateTokenAsync(UserClaims user);
    Task<string> GenerateRefreshTokenAsync();
    Task<ClaimsPrincipal?> GetPrincipalFromExpiredTokenAsync(string token);
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
    Task RevokeRefreshTokenAsync(string refreshToken);
    Task RevokeTokenAsync(string jti, string userId);
    Task RevokeAllUserTokensAsync(string userId);
}
