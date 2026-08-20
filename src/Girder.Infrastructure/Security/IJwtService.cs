using System.Security.Claims;

namespace Girder.Infrastructure.Security;

public interface IJwtService
{
    Task<TokenResult> GenerateTokenAsync(UserClaims user);
    Task<ClaimsPrincipal?> GetPrincipalFromExpiredTokenAsync(string token);
    Task<ClaimsPrincipal?> ValidateTokenAsync(string token);
    Task RevokeTokenAsync(string jti, string userId);
}
