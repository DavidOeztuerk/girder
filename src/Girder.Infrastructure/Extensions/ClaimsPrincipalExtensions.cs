using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Infrastructure.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string? GetUserId(this ClaimsPrincipal user)
    {
        return user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("user_id")?.Value
            ?? user.FindFirst("userId")?.Value
            ?? user.FindFirst("uid")?.Value
            ?? user.FindFirst("id")?.Value;
    }

    public static IEnumerable<string> GetRolesSafe(this ClaimsPrincipal user)
    {
        // Unterstützt ClaimTypes.Role/"role" (mehrfach) und optional "roles" (kommagetrennt)
        var roles =
            user.Claims.Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
                       .Select(c => c.Value)
            .Concat(
            user.Claims.Where(c => c.Type == "roles")
                       .SelectMany(c => c.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));

        return roles.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static string? GetEmail(this ClaimsPrincipal user)
    {
        return user.FindFirst("email")?.Value
               ?? user.FindFirst(ClaimTypes.Email)?.Value;
    }

    public static string? GetUsername(this ClaimsPrincipal user)
    {
        return user.FindFirst("username")?.Value
               ?? user.FindFirst("preferred_username")?.Value
               ?? user.FindFirst(ClaimTypes.Name)?.Value;
    }

    public static bool IsSuperAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole("SuperAdmin") || user.HasClaim("role", "SuperAdmin");
    }

    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        return user.IsInRole("Admin") || user.HasClaim("role", "Admin");
    }

    public static bool IsModerator(this ClaimsPrincipal user)
    {
        return user.IsInRole("Moderator") || user.HasClaim("role", "Moderator");
    }

    public static bool IsUser(this ClaimsPrincipal user)
    {
        return user.IsInRole("User") || user.HasClaim("role", "User");
    }
}
