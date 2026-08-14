namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when a user doesn't have sufficient permissions for an operation
/// </summary>
public class InsufficientPermissionsException : DomainException
{
    public string RequiredPermission { get; }
    public string? UserId { get; }

    public InsufficientPermissionsException(
        string requiredPermission,
        string? userId = null,
        string? message = null)
        : base(
            ErrorCodes.InsufficientPermissions,
            message ?? $"You don't have the required permission: {requiredPermission}",
            "Contact your administrator if you believe you should have access to this resource.",
            null,
            new Dictionary<string, object>
            {
                ["RequiredPermission"] = requiredPermission,
                ["UserId"] = userId ?? "Anonymous"
            })
    {
        RequiredPermission = requiredPermission;
        UserId = userId;
    }

    public override int GetHttpStatusCode() => 403; // Forbidden
}
