namespace Noelia.Infrastructure.Security.Identity;

/// <summary>
/// Claim types Noelia reads when building a <see cref="Noelia.Core.Identity.Principal"/>.
/// </summary>
public static class NoeliaClaimTypes
{
    /// <summary>
    /// The company the caller is acting for. Absent on a token issued to a
    /// person acting for themselves.
    /// </summary>
    public const string Tenant = "tenant";
}
