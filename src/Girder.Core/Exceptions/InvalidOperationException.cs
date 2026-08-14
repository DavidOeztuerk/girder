namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when an operation is invalid in the current state
/// </summary>
public class InvalidOperationException : DomainException
{
    public string Operation { get; }
    public string CurrentState { get; }

    public InvalidOperationException(
        string operation,
        string currentState,
        string? message = null,
        string? details = null)
        : base(
            ErrorCodes.InvalidOperation,
            message ?? $"Operation '{operation}' is not valid in the current state: {currentState}",
            details ?? "Please check the prerequisites for this operation.",
            null,
            new Dictionary<string, object>
            {
                ["Operation"] = operation,
                ["CurrentState"] = currentState
            })
    {
        Operation = operation;
        CurrentState = currentState;
    }

    public override int GetHttpStatusCode() => 400; // Bad Request
}