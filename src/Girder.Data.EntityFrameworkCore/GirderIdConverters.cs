using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Girder.Data.EntityFrameworkCore;

/// <summary>Maps <see cref="SubjectId"/> to the <see cref="Guid"/> column type.</summary>
public sealed class SubjectIdConverter : ValueConverter<SubjectId, Guid>
{
    public SubjectIdConverter()
        : base(id => id.Value, value => new SubjectId(value))
    {
    }
}

/// <summary>Maps <see cref="TenantId"/> to the <see cref="Guid"/> column type.</summary>
public sealed class TenantIdConverter : ValueConverter<TenantId, Guid>
{
    public TenantIdConverter()
        : base(id => id.Value, value => new TenantId(value))
    {
    }
}

public static class GirderIdConventions
{
    /// <summary>
    /// Maps Girder's typed identifiers to <see cref="Guid"/> columns.
    /// </summary>
    /// <remarks>
    /// Call from <c>DbContext.ConfigureConventions</c>. Without it EF cannot
    /// translate comparisons on <see cref="SubjectId"/> or <see cref="TenantId"/>.
    /// </remarks>
    public static ModelConfigurationBuilder AddGirderIdConverters(
        this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<SubjectId>().HaveConversion<SubjectIdConverter>();
        configurationBuilder.Properties<TenantId>().HaveConversion<TenantIdConverter>();

        return configurationBuilder;
    }
}
