using MediatR;
using Microsoft.AspNetCore.Http;

namespace CQRS.Extensions;

/// <summary>
/// Extensions for easy CQRS usage in controllers
/// </summary>
public static class MediatorExtensions
{
    /// <summary>
    /// Sends a command and handles the response
    /// </summary>
    public static async Task<IResult> SendCommand<T>(this IMediator mediator, T command)
        where T : class
    {
        var result = await mediator.Send(command);

        if (result != null && ((dynamic)result).Success == true)
        {
            return TypedResults.Ok(result);
        }
        else
        {
            return TypedResults.BadRequest(result);
        }
    }

    /// <summary>
    /// Sends a query and handles the response
    /// </summary>
    public static async Task<IResult> SendQuery<T>(this IMediator mediator, T query)
        where T : class
    {
        var result = await mediator.Send(query);

        if (result != null && ((dynamic)result).Success == true)
        {
            return TypedResults.Ok(result);
        }
        else
        {
            return TypedResults.BadRequest(result);
        }
    }
}