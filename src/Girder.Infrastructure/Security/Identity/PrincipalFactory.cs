using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Girder.Core.Identity;

namespace Girder.Infrastructure.Security.Identity;

/// <inheritdoc />
public sealed class PrincipalFactory : IPrincipalFactory
{
    /// <inheritdoc />
    public PrincipalResult Create(ClaimsPrincipal? user)
    {
        if (user?.Identity is not { IsAuthenticated: true })
        {
            return PrincipalResult.Anonymous();
        }

        var subjectClaim = user.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                           ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!SubjectId.TryParse(subjectClaim, out var subject))
        {
            return PrincipalResult.Invalid("The token carries no usable subject claim.");
        }

        var tenantClaim = user.FindFirst(GirderClaimTypes.Tenant)?.Value;

        if (tenantClaim is null)
        {
            return PrincipalResult.Success(Principal.Person(subject));
        }

        // A malformed tenant claim is rejected rather than downgraded to a
        // person, so that a broken issuer surfaces instead of silently
        // changing what the caller can see.
        if (!TenantId.TryParse(tenantClaim, out var tenant))
        {
            return PrincipalResult.Invalid("The token carries an unusable tenant claim.");
        }

        return PrincipalResult.Success(Principal.Company(subject, tenant));
    }
}
