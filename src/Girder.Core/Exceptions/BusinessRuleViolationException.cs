namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when a business rule is violated
/// </summary>
public class BusinessRuleViolationException : DomainException
{
    public string RuleName { get; }

    public BusinessRuleViolationException(
        string errorCode,
        string ruleName,
        string message,
        string? details = null,
        Dictionary<string, object>? additionalData = null)
        : base(errorCode, message, details, null, additionalData)
    {
        RuleName = ruleName;
    }

    public override int GetHttpStatusCode() => 422; // Unprocessable Entity
}