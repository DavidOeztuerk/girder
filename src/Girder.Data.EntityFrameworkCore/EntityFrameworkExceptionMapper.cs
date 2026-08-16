using System.Net;
using Girder.Abstractions.Diagnostics;
using Girder.Core.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Data.EntityFrameworkCore;

/// <summary>
/// Maps EF Core's persistence exceptions to responses.
/// </summary>
/// <remarks>
/// Lives here rather than in the exception handler so an application using
/// Dapper, raw SQL or no database at all does not inherit EF Core.
/// </remarks>
public sealed class EntityFrameworkExceptionMapper : IExceptionResponseMapper
{
    /// <inheritdoc />
    public ExceptionResponse? Map(Exception exception) => exception switch
    {
        DbUpdateConcurrencyException => new ExceptionResponse(
            HttpStatusCode.Conflict,
            "Concurrency Conflict",
            ErrorCodes.ConcurrencyConflict,
            "The resource was modified by another user. Please refresh and try again."),

        // The duplicate-key detection is a substring match on the driver's
        // message, so it depends on the provider's wording and its language.
        // A miss falls through to the generic database error below, which is
        // the safe direction: a wrong 409 would tell the caller to retry
        // something that cannot succeed.
        DbUpdateException { InnerException.Message: var message }
            when message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) =>
            new ExceptionResponse(
                HttpStatusCode.Conflict,
                "Duplicate Resource",
                ErrorCodes.DuplicateKey,
                "A resource with the same unique identifier already exists."),

        DbUpdateException => new ExceptionResponse(
            HttpStatusCode.InternalServerError,
            "Database Error",
            ErrorCodes.DatabaseError,
            "A database error occurred. Please try again later."),

        _ => null
    };
}

public static class EntityFrameworkExceptionMapping
{
    /// <summary>
    /// Teaches the exception handler about EF Core's persistence failures.
    /// Without it they surface as a plain 500.
    /// </summary>
    public static IServiceCollection AddEntityFrameworkExceptionMapping(this IServiceCollection services) =>
        services.AddSingleton<IExceptionResponseMapper, EntityFrameworkExceptionMapper>();
}
