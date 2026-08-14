namespace Infrastructure.Security;

/// <summary>
/// Standard authorization policies
/// </summary>
public static class Policies
{
    public const string RequireAdminRole = "RequireAdminRole";
    public const string RequireModeratorRole = "RequireModeratorRole";
    public const string RequireUserRole = "RequireUserRole";
    public const string RequireVerifiedEmail = "RequireVerifiedEmail";
    public const string RequireActiveAccount = "RequireActiveAccount";
    public const string CanManageUsers = "CanManageUsers";
    public const string CanManageSkills = "CanManageSkills";
    public const string CanViewSystemLogs = "CanViewSystemLogs";
}
