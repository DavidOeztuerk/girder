namespace Girder.Infrastructure.Security.Identity;

/// <summary>
/// Claim types Girder reads when building a <see cref="Girder.Core.Identity.Principal"/>.
/// </summary>
public static class GirderClaimTypes
{
    /// <summary>
    /// The company the caller is acting for. Absent on a token issued to a
    /// person acting for themselves.
    /// </summary>
    public const string Tenant = "tenant";
}
