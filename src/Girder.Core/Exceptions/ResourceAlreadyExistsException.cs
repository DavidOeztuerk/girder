namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when attempting to create a resource that already exists
/// </summary>
public class ResourceAlreadyExistsException(
    string resourceType,
    string duplicateField,
    string duplicateValue,
    string? message = null) : DomainException(
        ErrorCodes.ResourceAlreadyExists,
        message ?? $"A {resourceType.ToLower()} with {duplicateField} '{duplicateValue}' already exists.",
        $"Please choose a different {duplicateField.ToLower()} or use the existing {resourceType.ToLower()}.",
        null,
        new Dictionary<string, object>
            {
                ["ResourceType"] = resourceType,
                ["DuplicateField"] = duplicateField,
                ["DuplicateValue"] = duplicateValue
            })
{
    public string ResourceType { get; } = resourceType;
    public string DuplicateField { get; } = duplicateField;
    public string DuplicateValue { get; } = duplicateValue;

    public override int GetHttpStatusCode() => 409; // Conflict
}