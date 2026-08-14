using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace Infrastructure.Security;

/// <summary>
/// Middleware for checking token revocation
/// </summary>
public class TokenRevocationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITokenRevocationService _tokenRevocationService;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    public TokenRevocationMiddleware(
        RequestDelegate next,
        ITokenRevocationService tokenRevocationService,
        ILogger<TokenRevocationMiddleware> logger)
    {
        _next = next;
        _tokenRevocationService = tokenRevocationService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip for non-authenticated requests
        if (!context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        try
        {
            var jti = context.User.FindFirst("jti")?.Value;
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(jti))
            {
                var isRevoked = await _tokenRevocationService.IsTokenRevokedAsync(jti);
                if (isRevoked)
                {
                    _logger.LogWarning(
                        "Blocked request with revoked token: JTI={Jti}, UserId={UserId}, Path={Path}",
                        jti, userId, context.Request.Path);

                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("Token has been revoked");
                    return;
                }
            }

            // Check if all user tokens are revoked (user pattern)
            if (!string.IsNullOrEmpty(userId))
            {
                var userPattern = $"revoked:user:pattern:{userId}";
                var isUserRevoked = await _tokenRevocationService.IsTokenRevokedAsync(userPattern);
                if (isUserRevoked)
                {
                    _logger.LogWarning(
                        "Blocked request for user with revoked tokens: UserId={UserId}, Path={Path}",
                        userId, context.Request.Path);

                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsync("All user tokens have been revoked");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking token revocation status");
            // Continue processing - don't block valid requests due to revocation check errors
        }

        await _next(context);
    }
}

/// <summary>
/// Background service for cleaning up expired revoked tokens
/// </summary>
public class TokenCleanupService : BackgroundService
{
    private readonly ITokenRevocationService _tokenRevocationService;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(6); // Run every 6 hours

    public TokenCleanupService(
        ITokenRevocationService tokenRevocationService,
        ILogger<TokenCleanupService> logger)
    {
        _tokenRevocationService = tokenRevocationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Token cleanup service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Starting token cleanup process");
                await _tokenRevocationService.CleanupExpiredTokensAsync(stoppingToken);
                _logger.LogDebug("Token cleanup process completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token cleanup process");
            }

            try
            {
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Token cleanup service stopped");
    }
}

/// <summary>
/// Extension methods for using token revocation middleware
/// </summary>
public static class TokenRevocationMiddlewareExtensions
{
    /// <summary>
    /// Use token revocation middleware
    /// </summary>
    public static IApplicationBuilder UseTokenRevocation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<TokenRevocationMiddleware>();
    }
}