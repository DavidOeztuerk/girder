using Microsoft.EntityFrameworkCore;
using Noelia.Abstractions.Hosting;
using Noelia.Abstractions.Security.Sessions;
using Noelia.Data.EntityFrameworkCore.Sessions;

namespace Noelia.Data.EntityFrameworkCore;

/// <summary>Exposes Entity Framework refresh-token persistence in the composition.</summary>
public static class EntityFrameworkNoeliaModule
{
    public static NoeliaModule RefreshTokens => new("Data.EntityFrameworkCore.RefreshTokens");

    public static NoeliaBuilder UseEntityFrameworkRefreshTokens<TContext>(
        this NoeliaBuilder noelia)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(noelia);

        return noelia.Use(
            RefreshTokens,
            builder => builder.Services.AddEntityFrameworkRefreshTokens<TContext>(),
            contract => contract
                .Requires<TContext>(new NoeliaProviderHint(
                    "application database provider", $"AddDbContext<{typeof(TContext).Name}>()"))
                .Provides<IRefreshTokenStore>(
                    "Noelia.Data.EntityFrameworkCore",
                    $"UseEntityFrameworkRefreshTokens<{typeof(TContext).Name}>()"));
    }
}
