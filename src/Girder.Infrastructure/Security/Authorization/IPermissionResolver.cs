namespace Infrastructure.Security.Authorization;

/// <summary>
/// Interface for resolving permissions required for actions
/// </summary>
public interface IPermissionResolver
{
    /// <summary>
    /// Get permissions required for a specific action on a resource
    /// </summary>
    Task<IEnumerable<PermissionDefinition>> GetRequiredPermissionsAsync(string resourceType, string action);

    /// <summary>
    /// Get all owner permissions for a resource type
    /// </summary>
    Task<IEnumerable<string>> GetOwnerPermissionsAsync(string resourceType);

    /// <summary>
    /// Get all available permissions for a resource type
    /// </summary>
    Task<IEnumerable<PermissionDefinition>> GetAvailablePermissionsAsync(string resourceType);

    /// <summary>
    /// Register a permission definition
    /// </summary>
    void RegisterPermission(PermissionDefinition permission);

    /// <summary>
    /// Register multiple permission definitions
    /// </summary>
    void RegisterPermissions(IEnumerable<PermissionDefinition> permissions);
}

/// <summary>
/// Permission definition
/// </summary>
public class PermissionDefinition
{
    /// <summary>
    /// Permission name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Permission description
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Resource type this permission applies to
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Actions this permission enables
    /// </summary>
    public List<string> Actions { get; set; } = new();

    /// <summary>
    /// Whether this permission is conditional
    /// </summary>
    public bool IsConditional { get; set; }

    /// <summary>
    /// Condition for this permission
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Permission category
    /// </summary>
    public PermissionCategory Category { get; set; } = PermissionCategory.Standard;

    /// <summary>
    /// Minimum role required for this permission
    /// </summary>
    public string? MinimumRole { get; set; }

    /// <summary>
    /// Whether this permission is automatically granted to owners
    /// </summary>
    public bool IsOwnerPermission { get; set; }

    /// <summary>
    /// Permission priority (higher priority permissions override lower ones)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

/// <summary>
/// Permission categories
/// </summary>
public enum PermissionCategory
{
    /// <summary>
    /// Standard user permission
    /// </summary>
    Standard,

    /// <summary>
    /// Administrative permission
    /// </summary>
    Administrative,

    /// <summary>
    /// System permission
    /// </summary>
    System,

    /// <summary>
    /// Owner permission
    /// </summary>
    Owner,

    /// <summary>
    /// Conditional permission
    /// </summary>
    Conditional
}

/// <summary>
/// Skillswap-specific permission names
/// </summary>
public static class SkillswapPermissions
{
    // User permissions
    public const string USER_READ = "user:read";
    public const string USER_UPDATE = "user:update";
    public const string USER_DELETE = "user:delete";
    public const string USER_ADMIN = "user:admin";

    // Skill permissions
    public const string SKILL_READ = "skill:read";
    public const string SKILL_CREATE = "skill:create";
    public const string SKILL_UPDATE = "skill:update";
    public const string SKILL_DELETE = "skill:delete";
    public const string SKILL_ADMIN = "skill:admin";

    // Match permissions
    public const string MATCH_READ = "match:read";
    public const string MATCH_CREATE = "match:create";
    public const string MATCH_UPDATE = "match:update";
    public const string MATCH_DELETE = "match:delete";
    public const string MATCH_ACCEPT = "match:accept";
    public const string MATCH_REJECT = "match:reject";

    // Appointment permissions
    public const string APPOINTMENT_READ = "appointment:read";
    public const string APPOINTMENT_CREATE = "appointment:create";
    public const string APPOINTMENT_UPDATE = "appointment:update";
    public const string APPOINTMENT_DELETE = "appointment:delete";
    public const string APPOINTMENT_JOIN = "appointment:join";

    // Videocall permissions
    public const string VIDEOCALL_CREATE = "videocall:create";
    public const string VIDEOCALL_JOIN = "videocall:join";
    public const string VIDEOCALL_MODERATE = "videocall:moderate";
    public const string VIDEOCALL_RECORD = "videocall:record";

    // System permissions
    public const string SYSTEM_ADMIN = "system:admin";
    public const string SYSTEM_MONITOR = "system:monitor";
    public const string SYSTEM_BACKUP = "system:backup";
    public const string SYSTEM_CONFIG = "system:config";

    // Notification permissions
    public const string NOTIFICATION_SEND = "notification:send";
    public const string NOTIFICATION_ADMIN = "notification:admin";
}

/// <summary>
/// Skillswap resource types
/// </summary>
public static class SkillswapResources
{
    public const string USER = "User";
    public const string SKILL = "Skill";
    public const string MATCH = "Match";
    public const string APPOINTMENT = "Appointment";
    public const string VIDEOCALL = "Videocall";
    public const string NOTIFICATION = "Notification";
    public const string SYSTEM = "System";
}

/// <summary>
/// Skillswap actions
/// </summary>
public static class SkillswapActions
{
    public const string READ = "read";
    public const string CREATE = "create";
    public const string UPDATE = "update";
    public const string DELETE = "delete";
    public const string ACCEPT = "accept";
    public const string REJECT = "reject";
    public const string JOIN = "join";
    public const string MODERATE = "moderate";
    public const string RECORD = "record";
    public const string SEND = "send";
    public const string ADMIN = "admin";
    public const string MONITOR = "monitor";
    public const string BACKUP = "backup";
    public const string CONFIG = "config";
}