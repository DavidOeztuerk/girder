using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Security.Identity;
using Microsoft.AspNetCore.Builder;

namespace Girder.Infrastructure.Builder.Modules;

/// <summary>
/// Turns the verified claims of a request into a
/// <see cref="Girder.Core.Identity.Principal"/> the application can read.
/// </summary>
public static class PrincipalModule
{
    /// <summary>
    /// Registers principal resolution and the capacity policies. Pair it with
    /// <see cref="UsePrincipal"/>.
    /// </summary>
    /// <remarks>
    /// Independent of <c>AddJwtAuthentication()</c>: a service may verify
    /// tokens without ever building a principal — a gateway that only routes
    /// does exactly that — and a service may build one from claims another
    /// scheme established.
    /// </remarks>
    public static InfrastructureBuilder AddPrincipal(this InfrastructureBuilder builder)
    {
        builder.Services.AddGirderIdentity();
        return builder;
    }

    /// <summary>
    /// Resolves the principal once per request.
    /// </summary>
    /// <remarks>
    /// Belongs after <c>UseAuth()</c>: it reads the claims authentication
    /// established, and placed before it every request looks anonymous.
    /// </remarks>
    public static InfrastructureMiddlewareBuilder UsePrincipal(
        this InfrastructureMiddlewareBuilder builder) =>
        builder.Step(GirderModule.Principal, step =>
        {
            step.Requires<IPrincipalFactory>("UsePrincipal()", "AddPrincipal()");
            step.App.UseGirderPrincipal();
        });
}
