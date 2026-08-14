namespace Girder.Infrastructure.Security;

/// <summary>
/// Authorization policies registered by <c>AddGirderAuthorization</c>.
/// </summary>
/// <remarks>
/// Policies that turn on a specific permission are not listed here: every
/// permission in the application's <c>IPermissionCatalog</c> gets a policy of
/// the same name, so <c>[Authorize(Policy = "users:delete")]</c> works without
/// a constant.
/// </remarks>
public static class Policies
{
    public const string RequireAdminRole = "RequireAdminRole";
    public const string RequireModeratorRole = "RequireModeratorRole";
    public const string RequireUserRole = "RequireUserRole";
    public const string RequireVerifiedEmail = "RequireVerifiedEmail";
    public const string RequireActiveAccount = "RequireActiveAccount";
}
