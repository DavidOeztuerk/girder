using System.Security.Claims;
using Girder.Core.Identity;

namespace Girder.Infrastructure.Security.Identity;

/// <summary>
/// Translates the claims of an authenticated request into a
/// <see cref="Principal"/>.
/// </summary>
public interface IPrincipalFactory
{
    /// <summary>
    /// Builds a principal, or returns a failure describing why the claims could
    /// not be translated.
    /// </summary>
    PrincipalResult Create(ClaimsPrincipal? user);
}

/// <summary>
/// The outcome of translating claims into a <see cref="Principal"/>.
/// </summary>
public readonly record struct PrincipalResult
{
    private PrincipalResult(Principal? principal, string? error)
    {
        Principal = principal;
        Error = error;
    }

    /// <summary>The principal, when translation succeeded.</summary>
    public Principal? Principal { get; }

    /// <summary>Why translation failed, when it did.</summary>
    public string? Error { get; }

    /// <summary>The request carried no authenticated identity.</summary>
    public bool IsAnonymous => Principal is null && Error is null;

    /// <summary>The request carried an identity that could not be translated.</summary>
    public bool IsInvalid => Error is not null;

    public static PrincipalResult Anonymous() => new(null, null);

    public static PrincipalResult Success(Principal principal) => new(principal, null);

    public static PrincipalResult Invalid(string error) => new(null, error);
}
