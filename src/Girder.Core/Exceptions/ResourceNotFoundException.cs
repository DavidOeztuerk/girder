namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when a requested resource is not found
/// </summary>
public class ResourceNotFoundException : DomainException
{
    public string ResourceType { get; }
    public string ResourceId { get; }

    public ResourceNotFoundException(
        string resourceType,
        string resourceId,
        string? message = null)
        : base(
            ErrorCodes.ResourceNotFound,
            message ?? $"{resourceType} with ID '{resourceId}' was not found.",
            $"The requested {resourceType.ToLower()} does not exist or you don't have permission to access it.",
            null,
            new Dictionary<string, object> 
            { 
                ["ResourceType"] = resourceType,
                ["ResourceId"] = resourceId 
            })
    {
        ResourceType = resourceType;
        ResourceId = resourceId;
    }

    public override int GetHttpStatusCode() => 404; // Not Found
}