using Girder.Abstractions.Security;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Builder.Modules;

public static class JwtModule
{
    /// <summary>
    /// Adds JWT authentication: the bearer scheme that verifies tokens and the
    /// <see cref="IJwtService"/> that issues them.
    /// </summary>
    /// <remarks>
    /// One module for both sides because both read the same <c>JwtSettings</c>
    /// and sign with the same key. A service that only verifies simply never
    /// resolves the issuing side.
    /// <para>
    /// Needs a token revocation store. Register one from a provider package, or
    /// state with <c>AddNoTokenRevocation(rationale)</c> that this deployment
    /// does without.
    /// </para>
    /// </remarks>
    public static InfrastructureBuilder AddJwtAuthentication(this InfrastructureBuilder builder)
    {
        builder.JwtEnabled = true;

        builder.RequiresProvider<ITokenRevocationEvaluator>(
            "AddJwtAuthentication()",
            "AddInMemoryTokenRevocation(), AddRedisTokenRevocation(maxTokenLifetime) "
            + "or AddNoTokenRevocation(rationale)");
        builder.RequiresProvider<ITokenRevocationWriter>(
            "AddJwtAuthentication()",
            "AddInMemoryTokenRevocation() or AddRedisTokenRevocation(maxTokenLifetime)");

        builder.Services.AddScoped<IJwtService, JwtService>();
        builder.Services.AddJwtAuthentication(builder.Configuration, builder.Environment);

        return builder;
    }
}
