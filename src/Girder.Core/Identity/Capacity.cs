namespace Girder.Core.Identity;

/// <summary>
/// The capacity a <see cref="Principal"/> is acting in.
/// </summary>
/// <remarks>
/// A closed hierarchy: <see cref="AsSelf"/> and <see cref="ForCompany"/> are the
/// only cases and no further case can be added from outside, so pattern matches
/// over it are exhaustive.
/// </remarks>
public abstract record Capacity
{
    private Capacity() { }

    /// <summary>
    /// Acting for oneself. There is no tenant in this case.
    /// </summary>
    public sealed record AsSelf : Capacity
    {
        internal AsSelf() { }

        public static AsSelf Instance { get; } = new();

        public override string ToString() => "as self";
    }

    /// <summary>
    /// Acting on behalf of a company.
    /// </summary>
    /// <remarks>
    /// Carries no role. A token states which company the caller acts for, never
    /// with what rights; read the role from the membership store per operation.
    /// </remarks>
    public sealed record ForCompany : Capacity
    {
        /// <exception cref="ArgumentException">
        /// <paramref name="tenant"/> is <see cref="TenantId.None"/>.
        /// </exception>
        public ForCompany(TenantId tenant)
        {
            if (tenant.IsNone)
            {
                throw new ArgumentException(
                    "ForCompany requires a tenant. Use Capacity.AsSelf to express "
                    + "that a caller acts for no company.",
                    nameof(tenant));
            }

            Tenant = tenant;
        }

        public TenantId Tenant { get; }

        public override string ToString() => $"for company {Tenant}";
    }
}
