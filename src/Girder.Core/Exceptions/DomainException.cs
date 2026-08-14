namespace Core.Common.Exceptions;

/// <summary>
/// Base exception for all domain-related errors
/// </summary>
public abstract class DomainException : Exception
{
    public string ErrorCode { get; }
    public string? Details { get; }
    public Dictionary<string, object>? AdditionalData { get; }

    protected DomainException(
        string errorCode,
        string message,
        string? details = null,
        Exception? innerException = null,
        Dictionary<string, object>? additionalData = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = details;
        AdditionalData = additionalData;
    }

    public virtual int GetHttpStatusCode() => 400; // Bad Request by default
}