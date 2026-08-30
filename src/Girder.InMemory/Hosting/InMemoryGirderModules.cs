using Girder.Abstractions.Hosting;
using Girder.InMemory.Caching;
using Girder.InMemory.Sessions;

namespace Girder.InMemory.Hosting;

/// <summary>
/// The modules this package contributes to a Girder composition.
/// </summary>
/// <remarks>
/// Girder knows nothing about these. They reach a composition the way a database
/// provider reaches Entity Framework's options: through an extension method on
/// the builder, carrying their own registration with them.
/// </remarks>
public static class InMemoryGirderModules
{
    /// <summary>Caching, kept in this process.</summary>
    public static GirderModule Cache => new("InMemory.Cache");

    /// <summary>Refresh tokens, kept in this process.</summary>
    public static GirderModule RefreshTokens => new("InMemory.RefreshTokens");

    /// <summary>
    /// Keeps cached values in this process.
    /// </summary>
    /// <remarks>
    /// Nothing is shared between instances: a value written on one is invisible
    /// to the next, and an invalidation reaches only the instance that ran it.
    /// Right for a single instance and for tests; for anything else use the
    /// Redis package.
    /// <para>
    /// Without a cache module of some kind, the modules that need one refuse to
    /// start and name themselves while doing it.
    /// </para>
    /// </remarks>
    /// <param name="girder">The composition.</param>
    /// <param name="keyPrefix">
    /// Namespaces every key, so two services sharing a store cannot read each
    /// other's entries.
    /// </param>
    public static GirderBuilder UseInMemoryCache(this GirderBuilder girder, string keyPrefix)
    {
        ArgumentNullException.ThrowIfNull(girder);
        return girder.Use(Cache, g => g.Services.AddInMemoryCache(keyPrefix));
    }

    /// <summary>
    /// Keeps refresh tokens in this process.
    /// </summary>
    /// <remarks>
    /// Every restart signs everyone out, and a second instance does not know the
    /// tokens the first issued. For a deployment, keep them in the database with
    /// <c>AddEntityFrameworkRefreshTokens</c>.
    /// </remarks>
    public static GirderBuilder UseInMemoryRefreshTokens(this GirderBuilder girder)
    {
        ArgumentNullException.ThrowIfNull(girder);
        return girder.Use(RefreshTokens, g => g.Services.AddInMemoryRefreshTokens());
    }
}
