using Girder.Core.Identity;
using Microsoft.AspNetCore.Http;

namespace Girder.Infrastructure.Security.Identity;

/// <summary>
/// Reads the principal that <see cref="PrincipalMiddleware"/> resolved for the
/// current request.
/// </summary>
public sealed class HttpContextCurrentPrincipal : ICurrentPrincipal
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentPrincipal(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <inheritdoc />
    public Principal? Current =>
        _accessor.HttpContext?.Items.TryGetValue(PrincipalMiddleware.ItemKey, out var value) == true
            ? value as Principal
            : null;
}
