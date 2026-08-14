namespace Core.Common.Exceptions;

/// <summary>
/// Exception thrown when an external service call fails
/// </summary>
public class ExternalServiceException : Exception
{
    public string ServiceName { get; }
    public string? Endpoint { get; }
    public int? StatusCode { get; }
    public string ErrorCode { get; }

    public ExternalServiceException(
        string serviceName,
        string message,
        string? endpoint = null,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ServiceName = serviceName;
        Endpoint = endpoint;
        StatusCode = statusCode;
        ErrorCode = ErrorCodes.ExternalServiceError;
    }

    public int GetHttpStatusCode() => 503; // Service Unavailable
}