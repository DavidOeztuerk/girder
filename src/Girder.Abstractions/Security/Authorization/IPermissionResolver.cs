using Girder.Abstractions.Security.Authorization;
namespace Girder.Abstractions.Security.Authorization;

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
/// Girder actions
/// </summary>
public static class GirderActions
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