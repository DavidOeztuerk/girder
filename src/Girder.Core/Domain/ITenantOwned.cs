using Girder.Core.Identity;

namespace Girder.Core.Domain;

/// <summary>
/// Marks an entity as owned by a company, which enables the tenant query filter
/// for it.
/// </summary>
/// <remarks>
/// <para>
/// Multi-tenancy is opt-in per entity, not per application. Company data such as
/// job postings, teams and company profiles implements this interface.
/// </para>
/// <para>
/// Data belonging to a person — profile, résumé, portfolio, consent — must not.
/// It is keyed by <see cref="SubjectId"/> and follows the person across
/// employers; filtering it by tenant would hide it after a job change or expose
/// it to a new employer.
/// </para>
/// <para>
/// Girder supplies the marker and the model-builder extension. Each service owns
/// its own <c>DbContext</c> and applies the filter there.
/// </para>
/// </remarks>
public interface ITenantOwned
{
    /// <summary>
    /// The owning company. Never <see cref="TenantId.None"/>; enforce this with a
    /// check constraint, since the tenant filter relies on it.
    /// </summary>
    TenantId Tenant { get; }
}
