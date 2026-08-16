using System.Net;

namespace Girder.Abstractions.Diagnostics;

/// <summary>
/// How an exception should be reported to the caller.
/// </summary>
/// <param name="Status">HTTP status code.</param>
/// <param name="Title">Short, stable heading.</param>
/// <param name="ErrorCode">Machine-readable code the caller can branch on.</param>
/// <param name="FallbackDetail">
/// Wording used when the application configured none for
/// <paramref name="ErrorCode"/>. Never include the exception message: it
/// reaches the caller and may carry connection strings or row contents.
/// </param>
public readonly record struct ExceptionResponse(
    HttpStatusCode Status,
    string Title,
    string ErrorCode,
    string FallbackDetail);

/// <summary>
/// Turns an infrastructure-specific exception into a response.
/// </summary>
/// <remarks>
/// The exception handler knows framework and domain exceptions. A provider
/// knows its own — a database driver throws types the handler has no reason to
/// reference — so each provider package registers a mapper instead of the
/// handler naming every driver it might meet.
/// <para>
/// Mappers are asked in registration order; the first non-null answer wins.
/// Returning null means "not mine", which is the normal case.
/// </para>
/// </remarks>
public interface IExceptionResponseMapper
{
    /// <summary>Maps <paramref name="exception"/>, or returns null to pass.</summary>
    ExceptionResponse? Map(Exception exception);
}
