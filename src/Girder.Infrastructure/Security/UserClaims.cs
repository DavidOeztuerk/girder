using Girder.Core.Identity;

namespace Girder.Infrastructure.Security;

/// <summary>
/// The facts a token is issued for.
/// </summary>
public class UserClaims
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    /// <summary>
    /// Whether the address was confirmed, or <c>null</c> when the caller does not
    /// state it.
    /// </summary>
    /// <remarks>
    /// <strong>No default, and that is the whole point.</strong> It used to be
    /// <c>false</c>, so anyone filling this in without knowing the field existed
    /// — and nothing made them know — issued a token saying the address was
    /// unconfirmed. Measured on a real run: an address confirmed seconds earlier
    /// carried <c>email_verified: false</c>. <c>EmailVerifiedHandler</c> reads
    /// this claim, so the policy locked out everyone, including the people it was
    /// written to admit.
    /// <para>
    /// Left unset, the claim is not written and the policy refuses for want of an
    /// answer. A missing statement may lead to no; it must never lead to yes.
    /// </para>
    /// </remarks>
    public bool? EmailVerified { get; set; }

    /// <summary>
    /// The account's standing, or <c>null</c> when the caller does not state it.
    /// </summary>
    /// <remarks>
    /// <strong>No default, and this is the dangerous half.</strong> It used to be
    /// <c>"Active"</c>, which <c>ActiveAccountHandler</c> reads as "let them
    /// through" — so the policy that exists to stop a suspended or deleted account
    /// let every one of them through, while looking exactly like a check. A
    /// constant wearing the shape of a decision.
    /// <para>
    /// Left unset, the claim is not written and the policy refuses.
    /// </para>
    /// </remarks>
    public string? AccountStatus { get; set; }

    /// <summary>
    /// The capacity the token is issued for. Defaults to acting for oneself.
    /// Set <see cref="Capacity.ForCompany"/> only after verifying membership,
    /// since the resulting claim is what downstream services trust.
    /// </summary>
    public Capacity Acting { get; set; } = Capacity.AsSelf.Instance;

    // Session management
    public string? SessionId { get; set; }

    /// <summary>
    /// Extra single-valued claims, written as they are given.
    /// </summary>
    /// <remarks>
    /// One value per name. A consumer expecting a JSON array cannot be served
    /// from here — see <see cref="CustomClaimArrays"/>.
    /// </remarks>
    public Dictionary<string, string>? CustomClaims { get; set; }

    /// <summary>
    /// Extra claims that have to arrive as a JSON array.
    /// </summary>
    /// <remarks>
    /// <para>Each value becomes its own claim under the same name, which is how a
    /// JWT expresses an array — the same mechanism Girder's own roles and
    /// permissions have always used.</para>
    /// <para><see cref="CustomClaims"/> could not express this: a dictionary of
    /// strings has one value per key, so <c>["user", "admin"]</c> came out either
    /// as the string <c>"user,admin"</c> or not at all. A consumer validating the
    /// shape then rejected the token, and reproducing one claim in the form an
    /// existing consumer expects is the entire purpose of a free claim field.</para>
    /// <para>A name appearing in both collections gets both: the single value and
    /// the array entries, in that order.</para>
    /// </remarks>
    public Dictionary<string, IReadOnlyList<string>>? CustomClaimArrays { get; set; }
}
