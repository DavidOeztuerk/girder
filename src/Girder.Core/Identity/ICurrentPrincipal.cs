using System.Diagnostics.CodeAnalysis;

namespace Girder.Core.Identity;

/// <summary>
/// Access to the principal of the current request.
/// </summary>
public interface ICurrentPrincipal
{
    /// <summary><c>null</c> when the request is not authenticated.</summary>
    Principal? Current { get; }
}

public static class CurrentPrincipalExtensions
{
    public static bool IsAuthenticated(this ICurrentPrincipal accessor) =>
        accessor.Current is not null;

    /// <summary>
    /// The principal of the current request.
    /// </summary>
    /// <remarks>
    /// For code behind <c>[Authorize]</c>, where an anonymous request is a wiring
    /// mistake rather than a runtime case.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The request is anonymous.</exception>
    public static Principal Require(this ICurrentPrincipal accessor) =>
        accessor.Current ?? throw new InvalidOperationException(
            "No principal on this request. Is [Authorize] or the principal "
            + "middleware missing?");

    /// <summary>
    /// Gets the tenant when the caller acts for a company. Anonymous callers and
    /// callers acting as a person both return false.
    /// </summary>
    public static bool TryGetTenant(this ICurrentPrincipal accessor, out TenantId tenant)
    {
        if (accessor.Current is { } principal)
        {
            return principal.TryGetTenant(out tenant);
        }

        tenant = TenantId.None;
        return false;
    }

    public static bool TryGetSubject(this ICurrentPrincipal accessor, [NotNullWhen(true)] out SubjectId subject)
    {
        if (accessor.Current is { } principal)
        {
            subject = principal.Subject;
            return true;
        }

        subject = default;
        return false;
    }
}
