using Girder.Core.Identity;

namespace Girder.Infrastructure.Security;

/// <summary>
/// The facts a token is issued for.
/// </summary>
public class UserClaims
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    /// <summary>Not emitted into the token and read by nothing.</summary>
    [Obsolete("Never written into the token. Carry display names in your own type; "
              + "this property is removed in the next major version.")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Not emitted into the token and read by nothing.</summary>
    [Obsolete("Never written into the token. Carry display names in your own type; "
              + "this property is removed in the next major version.")]
    public string LastName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public bool EmailVerified { get; set; } = false;
    public string AccountStatus { get; set; } = "Active";

    /// <summary>
    /// The capacity the token is issued for. Defaults to acting for oneself.
    /// Set <see cref="Capacity.ForCompany"/> only after verifying membership,
    /// since the resulting claim is what downstream services trust.
    /// </summary>
    public Capacity Acting { get; set; } = Capacity.AsSelf.Instance;

    // Session management
    public string? SessionId { get; set; }

    public Dictionary<string, string>? CustomClaims { get; set; }
}
