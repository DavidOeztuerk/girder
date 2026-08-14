// namespace Contracts.Common;

// /// <summary>
// /// Standard API error response format following RFC 7807 Problem Details
// /// </summary>
// /// <param name="Type">A URI reference that identifies the problem type</param>
// /// <param name="Title">A short, human-readable summary of the problem type</param>
// /// <param name="Status">HTTP status code</param>
// /// <param name="Detail">A human-readable explanation specific to this occurrence</param>
// /// <param name="Instance">A URI reference that identifies the specific occurrence</param>
// /// <param name="Errors">Validation errors (if applicable)</param>
// /// <param name="TraceId">Trace identifier for debugging</param>
// /// <param name="Timestamp">When the error occurred</param>
// public record ApiErrorResponse(
//     string Type,
//     string Title,
//     int Status,
//     string? Detail = null,
//     string? Instance = null,
//     Dictionary<string, string[]>? Errors = null,
//     string? TraceId = null,
//     DateTime? Timestamp = null) : IVersionedContract
// {
//     /// <summary>
//     /// API Version this response supports
//     /// </summary>
//     public string ApiVersion => "v1";

//     /// <summary>
//     /// Creates a validation error response
//     /// </summary>
//     public static ApiErrorResponse ValidationError(
//         Dictionary<string, string[]> errors,
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null)
//     {
//         return new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
//             Title: "One or more validation errors occurred.",
//             Status: 400,
//             Detail: detail ?? "The request contains invalid data.",
//             Instance: instance,
//             Errors: errors,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );
//     }

//     /// <summary>
//     /// Creates a not found error response
//     /// </summary>
//     public static ApiErrorResponse NotFound(
//         string resource,
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null)
//     {
//         return new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
//             Title: "Resource not found.",
//             Status: 404,
//             Detail: detail ?? $"The requested {resource} was not found.",
//             Instance: instance,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );
//     }

//     /// <summary>
//     /// Creates an unauthorized error response
//     /// </summary>
//     public static ApiErrorResponse Unauthorized(
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null)
//     {
//         return new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc7235#section-3.1",
//             Title: "Unauthorized.",
//             Status: 401,
//             Detail: detail ?? "Authentication is required to access this resource.",
//             Instance: instance,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );
//     }

//     /// <summary>
//     /// Creates a forbidden error response
//     /// </summary>
//     public static ApiErrorResponse Forbidden(
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null)
//     {
//         return new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
//             Title: "Forbidden.",
//             Status: 403,
//             Detail: detail ?? "You do not have permission to access this resource.",
//             Instance: instance,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );
//     }

//     /// <summary>
//     /// Creates a conflict error response
//     /// </summary>
//     public static ApiErrorResponse Conflict(
//         string resource,
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null)
//     {
//         return new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc7231#section-6.5.8",
//             Title: "Conflict.",
//             Status: 409,
//             Detail: detail ?? $"The {resource} already exists or conflicts with existing data.",
//             Instance: instance,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );
//     }

//     /// <summary>
//     /// Creates an internal server error response
//     /// </summary>
//     public static ApiErrorResponse InternalServerError(
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null)
//     {
//         return new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
//             Title: "An error occurred while processing your request.",
//             Status: 500,
//             Detail: detail ?? "An unexpected error occurred. Please try again later.",
//             Instance: instance,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );
//     }

//     /// <summary>
//     /// Creates a rate limit exceeded error response
//     /// </summary>
//     public static ApiErrorResponse TooManyRequests(
//         string? detail = null,
//         string? instance = null,
//         string? traceId = null,
//         TimeSpan? retryAfter = null)
//     {
//         var errorResponse = new ApiErrorResponse(
//             Type: "https://tools.ietf.org/html/rfc6585#section-4",
//             Title: "Too many requests.",
//             Status: 429,
//             Detail: detail ?? "Rate limit exceeded. Please try again later.",
//             Instance: instance,
//             TraceId: traceId,
//             Timestamp: DateTime.UtcNow
//         );

//         return errorResponse;
//     }
// }