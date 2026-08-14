using System.Linq.Expressions;
using Girder.Core.Domain;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Girder.Infrastructure.Data;

public static class TenantFilterExtensions
{
    /// <summary>
    /// The name of the query filter added by
    /// <see cref="ApplyTenantFilters(ModelBuilder, Expression{Func{TenantId}}, string)"/>.
    /// Pass it to <c>IgnoreQueryFilters</c> to disable tenant filtering for a
    /// single query.
    /// </summary>
    public const string TenantFilterName = "GirderTenant";

    /// <summary>
    /// Adds a tenant query filter to every entity implementing
    /// <see cref="ITenantOwned"/>. Entities without the interface are untouched.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="currentTenant">
    /// How to read the tenant of the current request. Must reference a member of
    /// the <see cref="DbContext"/> — for example
    /// <c>() =&gt; CurrentTenant</c> — so that EF re-evaluates it per query
    /// rather than capturing the value while the model is built.
    /// </param>
    /// <param name="filterName">
    /// Name of the filter. Named filters coexist with others on the same entity,
    /// such as a soft-delete filter.
    /// </param>
    /// <example>
    /// <code>
    /// protected override void OnModelCreating(ModelBuilder modelBuilder)
    /// {
    ///     modelBuilder.Entity&lt;Job&gt;().HasQueryFilter("SoftDeletion", j =&gt; !j.IsDeleted);
    ///     modelBuilder.ApplyTenantFilters(() =&gt; CurrentTenant);
    /// }
    ///
    /// private TenantId CurrentTenant =&gt; _principal.Current?.TenantForQueryFilter ?? TenantId.None;
    /// </code>
    /// </example>
    public static ModelBuilder ApplyTenantFilters(
        this ModelBuilder modelBuilder,
        Expression<Func<TenantId>> currentTenant,
        string filterName = TenantFilterName)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(currentTenant);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var entity = Expression.Parameter(entityType.ClrType, "e");
            var tenant = Expression.Property(entity, nameof(ITenantOwned.Tenant));
            var filter = Expression.Lambda(
                Expression.Equal(tenant, currentTenant.Body),
                entity);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filterName, filter);
        }

        return modelBuilder;
    }
}
