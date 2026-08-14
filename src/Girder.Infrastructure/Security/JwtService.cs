using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Girder.Core.Identity;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.Security.Authorization;
using Girder.Infrastructure.Security.Identity;
using System.Text.RegularExpressions;

namespace Girder.Infrastructure.Security;

public class JwtService : IJwtService
{
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<JwtService> _logger;
    private readonly ITokenRevocationService _tokenRevocationService;
    private readonly IPermissionCatalog _permissions;
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public JwtService(
        IOptions<JwtSettings> jwtSettings,
        ILogger<JwtService> logger,
        ITokenRevocationService tokenRevocationService,
        IPermissionCatalog? permissions = null)
    {
        _jwtSettings = jwtSettings.Value;
        _logger = logger;
        _tokenRevocationService = tokenRevocationService;
        _permissions = permissions ?? PermissionCatalog.Empty;
        ValidateJwtSettings();
    }

    private void ValidateJwtSettings()
    {
        if (string.IsNullOrWhiteSpace(_jwtSettings.Secret) || _jwtSettings.Secret.Length < 32)
        {
            throw new InvalidOperationException("JWT Secret must be at least 32 characters long");
        }

        if (string.IsNullOrWhiteSpace(_jwtSettings.Issuer))
        {
            throw new InvalidOperationException("JWT Issuer is required");
        }

        if (string.IsNullOrWhiteSpace(_jwtSettings.Audience))
        {
            throw new InvalidOperationException("JWT Audience is required");
        }

        // Allow negative values for testing expired tokens
        if (_jwtSettings.ExpireMinutes == 0)
        {
            throw new InvalidOperationException("JWT ExpireMinutes cannot be 0");
        }
    }

    public async Task<TokenResult> GenerateTokenAsync(UserClaims user)
    {
        ValidateUserClaims(user);

        // Merge role permissions with explicit user permissions
        user.Permissions = [.. _permissions
            .PermissionsFor(user.Roles)
            .Union(user.Permissions ?? Enumerable.Empty<string>())
            .Distinct()];

        var jti = Guid.NewGuid().ToString();
        var accessToken = await GenerateAccessTokenAsync(user, jti);
        var refreshToken = await GenerateRefreshTokenAsync();
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpireMinutes);

        _logger.LogInformation("Generated tokens for user {UserId} with roles {Roles} and JTI {Jti}",
            user.UserId, string.Join(", ", user.Roles), jti);

        return new TokenResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            TokenType = "Bearer"
        };
    }

    private void ValidateUserClaims(UserClaims user)
    {
        if (string.IsNullOrWhiteSpace(user.UserId))
        {
            throw new ArgumentException("UserId is required", nameof(user));
        }

        if (string.IsNullOrWhiteSpace(user.Email) || !EmailRegex.IsMatch(user.Email))
        {
            throw new ArgumentException("Valid email is required", nameof(user));
        }

        if (string.IsNullOrWhiteSpace(user.FirstName))
        {
            throw new ArgumentException("FirstName is required", nameof(user));
        }

        if (string.IsNullOrWhiteSpace(user.LastName))
        {
            throw new ArgumentException("LastName is required", nameof(user));
        }
    }

    private async Task<string> GenerateAccessTokenAsync(UserClaims user, string jti)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret))
        {
            KeyId = "GirderKey"
        };

        var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId),
            new(ClaimTypes.NameIdentifier, user.UserId),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new("email_verified", user.EmailVerified.ToString(), ClaimValueTypes.Boolean),
            new("account_status", user.AccountStatus)
        };

        // Add session claim for concurrent session control
        if (!string.IsNullOrEmpty(user.SessionId))
        {
            claims.Add(new("session_id", user.SessionId));
        }

        // Emitted only when acting for a company. Its absence is what marks a
        // token as belonging to a person acting for themselves.
        if (user.Acting is Capacity.ForCompany company)
        {
            claims.Add(new(GirderClaimTypes.Tenant, company.Tenant.ToString()));
        }

        // Roles the caller holds, plus every role those inherit.
        var rolesToAdd = new HashSet<string>(user.Roles);

        foreach (var role in user.Roles)
        {
            foreach (var inherited in _permissions.RolesInheritedBy(role))
            {
                rolesToAdd.Add(inherited);
            }
        }

        foreach (var role in rolesToAdd)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        // Add permission claims
        foreach (var permission in user.Permissions)
        {
            claims.Add(new Claim("permission", permission));
        }

        // Add custom claims
        if (user.CustomClaims != null)
        {
            foreach (var customClaim in user.CustomClaims)
            {
                claims.Add(new Claim(customClaim.Key, customClaim.Value));
            }
        }

        var securityToken = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            expires: DateTime.UtcNow.AddMinutes(_jwtSettings.ExpireMinutes),
            claims: claims,
            signingCredentials: signingCredentials);

        return await Task.FromResult(new JwtSecurityTokenHandler().WriteToken(securityToken));
    }

    public async Task<string> GenerateRefreshTokenAsync()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);

        var refreshToken = Convert.ToBase64String(randomNumber);

        _logger.LogDebug("Generated new refresh token");

        return await Task.FromResult(refreshToken);
    }

    public async Task<ClaimsPrincipal?> GetPrincipalFromExpiredTokenAsync(string token)
    {
        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret)),
            ValidateLifetime = false, // Don't validate expiry for refresh token scenario
            ValidIssuer = _jwtSettings.Issuer,
            ValidAudience = _jwtSettings.Audience,
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogWarning("Invalid token algorithm or format");
                return null;
            }

            return await Task.FromResult(principal);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract principal from expired token");
            return null;
        }
    }

    public async Task<ClaimsPrincipal?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret)),
            ValidateLifetime = true,
            ValidIssuer = _jwtSettings.Issuer,
            ValidAudience = _jwtSettings.Audience,
            ClockSkew = TimeSpan.Zero
        };

        var tokenHandler = new JwtSecurityTokenHandler();

        try
        {
            var principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out var securityToken);

            // Check if token is revoked
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrEmpty(jti) && await _tokenRevocationService.IsTokenRevokedAsync(jti))
            {
                _logger.LogWarning("Token with JTI {Jti} has been revoked", jti);
                return null;
            }

            // Additional security checks
            if (securityToken is not JwtSecurityToken jwtSecurityToken ||
                !jwtSecurityToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.LogWarning("Invalid token algorithm or format");
                return null;
            }

            return principal;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogDebug("Token has expired");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return null;
        }
    }

    public async Task RevokeTokenAsync(string jti, string userId)
    {
        var request = new TokenRevocationRequest
        {
            Jti = jti,
            UserId = userId,
            Reason = TokenRevocationReason.UserRequested,
            TokenExpiry = TimeSpan.FromMinutes(_jwtSettings.ExpireMinutes)
        };
        
        await _tokenRevocationService.RevokeTokenAsync(request);
        _logger.LogInformation("Token with JTI {Jti} revoked for user {UserId}", jti, userId);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        await _tokenRevocationService.RevokeRefreshTokenAsync(refreshToken);
        _logger.LogInformation("Refresh token revoked");
    }

    public async Task RevokeAllUserTokensAsync(string userId)
    {
        await _tokenRevocationService.RevokeUserTokensAsync(userId);
        _logger.LogInformation("All tokens revoked for user {UserId}", userId);
    }
}
