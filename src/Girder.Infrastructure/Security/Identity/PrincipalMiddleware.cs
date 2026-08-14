using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security.Identity;

/// <summary>
/// Translates the authenticated claims of a request into a
/// <see cref="Principal"/> once, and stores it for the rest of the pipeline.
/// </summary>
/// <remarks>
/// Register after authentication and before authorization. A request whose
/// claims cannot be translated is answered with 401 rather than continuing
/// without a principal.
/// </remarks>
public sealed class PrincipalMiddleware
{
    internal const string ItemKey = "Girder.Principal";

    private readonly RequestDelegate _next;
    private readonly IPrincipalFactory _factory;
    private readonly ILogger<PrincipalMiddleware> _logger;

    public PrincipalMiddleware(
        RequestDelegate next,
        IPrincipalFactory factory,
        ILogger<PrincipalMiddleware> logger)
    {
        _next = next;
        _factory = factory;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var result = _factory.Create(context.User);

        if (result.IsInvalid)
        {
            _logger.LogWarning("Rejecting request: {Reason}", result.Error);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (result.Principal is { } principal)
        {
            context.Items[ItemKey] = principal;
        }

        await _next(context);
    }
}
