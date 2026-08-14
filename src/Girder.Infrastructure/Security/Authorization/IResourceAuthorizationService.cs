using System.Security.Claims;

namespace Infrastructure.Security.Authorization;

/// <summary>
/// Interface for resource-based authorization
/// </summary>
public interface IResourceAuthorizationService
{
    /// <summary>
    /// Check if user can access a specific resource
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, 
        string resource, 
        string action, 
        object? resourceData = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check resource ownership
    /// </summary>
    Task<bool> IsResourceOwnerAsync(
        string userId, 
        string resourceType, 
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get user permissions for a resource
    /// </summary>
    Task<IEnumerable<string>> GetUserPermissionsAsync(
        string userId, 
        string resourceType, 
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grant permission to user for a resource
    /// </summary>
    Task GrantPermissionAsync(
        string userId, 
        string resourceType, 
        string resourceId, 
        string permission,
        string grantedBy,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke permission from user for a resource
    /// </summary>
    Task RevokePermissionAsync(
        string userId, 
        string resourceType, 
        string resourceId, 
        string permission,
        string revokedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if user has specific permission
    /// </summary>
    Task<bool> HasPermissionAsync(
        string userId, 
        string permission,
        string? resourceType = null,
        string? resourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all resources accessible by user
    /// </summary>
    Task<IEnumerable<ResourceAccess>> GetAccessibleResourcesAsync(
        string userId,
        string resourceType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authorization result
/// </summary>
public class AuthorizationResult
{
    /// <summary>
    /// Whether authorization succeeded
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Failure reasons
    /// </summary>
    public List<string> FailureReasons { get; set; } = new();

    /// <summary>
    /// Additional context
    /// </summary>
    public Dictionary<string, object?> Context { get; set; } = new();

    /// <summary>
    /// Create successful result
    /// </summary>
    public static AuthorizationResult Success() => new() { Succeeded = true };

    /// <summary>
    /// Create failed result
    /// </summary>
    public static AuthorizationResult Fail(string reason) => new() 
    { 
        Succeeded = false, 
        FailureReasons = new List<string> { reason } 
    };

    /// <summary>
    /// Create failed result with multiple reasons
    /// </summary>
    public static AuthorizationResult Fail(IEnumerable<string> reasons) => new() 
    { 
        Succeeded = false, 
        FailureReasons = reasons.ToList() 
    };
}

/// <summary>
/// Resource access information
/// </summary>
public class ResourceAccess
{
    /// <summary>
    /// Resource type
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Resource ID
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Permissions granted
    /// </summary>
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// Access granted at
    /// </summary>
    public DateTime GrantedAt { get; set; }

    /// <summary>
    /// Access expires at
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Who granted the access
    /// </summary>
    public string GrantedBy { get; set; } = string.Empty;

    /// <summary>
    /// Whether user is the owner
    /// </summary>
    public bool IsOwner { get; set; }
}

/// <summary>
/// Permission grant information
/// </summary>
public class PermissionGrant
{
    /// <summary>
    /// Grant ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User ID
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Resource type
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Resource ID
    /// </summary>
    public string ResourceId { get; set; } = string.Empty;

    /// <summary>
    /// Permission name
    /// </summary>
    public string Permission { get; set; } = string.Empty;

    /// <summary>
    /// When the permission was granted
    /// </summary>
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the permission expires
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Who granted the permission
    /// </summary>
    public string GrantedBy { get; set; } = string.Empty;

    /// <summary>
    /// Whether the grant is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object?> Metadata { get; set; } = new();
}