namespace Girder.Infrastructure.Security;

/// <summary>
/// Permissions for fine-grained access control
/// </summary>
public static class Permissions
{
    // System Permissions (Super Admin only)
    public const string SystemManageAll = "system:manage_all";
    public const string SystemViewLogs = "system:view_logs";
    public const string SystemManageSettings = "system:manage_settings";
    public const string SystemManageIntegrations = "system:manage_integrations";

    // User Management
    public const string UsersCreate = "users:create";
    public const string UsersRead = "users:read";
    public const string UsersReadOwn = "users:read_own";
    public const string UsersUpdate = "users:update";
    public const string UsersUpdateOwn = "users:update_own";
    public const string UsersDelete = "users:delete";
    public const string UsersViewAll = "users:view_all";
    public const string UsersViewReported = "users:view_reported";
    public const string UsersBlock = "users:block";
    public const string UsersUnblock = "users:unblock";
    public const string UsersManageRoles = "users:manage_roles";

    // Profile Management
    public const string ProfileViewOwn = "profile:view_own";
    public const string ProfileUpdateOwn = "profile:update_own";
    public const string ProfileViewAny = "profile:view_any";

    // Skill Management
    public const string SkillsCreateOwn = "skills:create_own";
    public const string SkillsUpdateOwn = "skills:update_own";
    public const string SkillsDeleteOwn = "skills:delete_own";
    public const string SkillsVerify = "skills:verify";
    public const string SkillsManageCategories = "skills:manage_categories";
    public const string SkillsManageProficiency = "skills:manage_proficiency";
    public const string SkillsViewAll = "skills:view_all";

    // Appointments
    public const string AppointmentsCreate = "appointments:create";
    public const string AppointmentsViewOwn = "appointments:view_own";
    public const string AppointmentsCancelOwn = "appointments:cancel_own";
    public const string AppointmentsViewAll = "appointments:view_all";
    public const string AppointmentsCancelAny = "appointments:cancel_any";
    public const string AppointmentsManage = "appointments:manage";

    // Matching
    public const string MatchingAccess = "matching:access";
    public const string MatchingViewAll = "matching:view_all";
    public const string MatchingManage = "matching:manage";

    // Reviews
    public const string ReviewsCreate = "reviews:create";
    public const string ReviewsModerate = "reviews:moderate";
    public const string ReviewsDelete = "reviews:delete";

    // Messages
    public const string MessagesSend = "messages:send";
    public const string MessagesViewOwn = "messages:view_own";
    public const string MessagesViewAll = "messages:view_all";

    // Video Calls
    public const string VideoCallsAccess = "videocalls:access";
    public const string VideoCallsManage = "videocalls:manage";

    // Content Moderation
    public const string ContentModerate = "content:moderate";
    public const string ReportsHandle = "reports:handle";
    public const string ReportsViewAll = "reports:view_all";

    // Admin Panel
    public const string AdminAccessDashboard = "admin:access_dashboard";
    public const string AdminViewStatistics = "admin:view_statistics";
    public const string AdminManageAll = "admin:manage_all";

    // Moderator Panel
    public const string ModeratorAccessPanel = "moderator:access_panel";

    // Security Alerts
    public const string SecurityViewAlerts = "security:view_alerts";
    public const string SecurityManageAlerts = "security:manage_alerts";

    // Verification Documents
    public const string VerificationsView = "verifications:view";
    public const string VerificationsReview = "verifications:review";

    // Role Management (Super Admin only)
    public const string RolesCreate = "roles:create";
    public const string RolesUpdate = "roles:update";
    public const string RolesDelete = "roles:delete";
    public const string RolesView = "roles:view";
    public const string PermissionsView = "permissions:view";
    public const string PermissionsManage = "permissions:manage";
}
