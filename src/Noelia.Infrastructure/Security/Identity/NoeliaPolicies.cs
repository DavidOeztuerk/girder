namespace Noelia.Infrastructure.Security.Identity;

/// <summary>
/// Authorization policies covering the capacity a caller is acting in.
/// </summary>
public static class NoeliaPolicies
{
    /// <summary>
    /// The caller must be acting for a company. Use on endpoints that read or
    /// write company data.
    /// </summary>
    public const string ActingForCompany = "Noelia.ActingForCompany";

    /// <summary>
    /// The caller must be acting for themselves. Use on endpoints that operate
    /// on a person's own data, so that a company token cannot reach them.
    /// </summary>
    public const string ActingAsSelf = "Noelia.ActingAsSelf";
}
