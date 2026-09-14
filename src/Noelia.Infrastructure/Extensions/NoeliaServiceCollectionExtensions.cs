using Noelia.Abstractions.Hosting;
using Noelia.Infrastructure.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Noelia.Infrastructure.Extensions;

/// <summary>
/// The entry point.
/// </summary>
public static class NoeliaServiceCollectionExtensions
{
    /// <summary>
    /// Sets up the Noelia modules this service asks for.
    /// </summary>
    /// <remarks>
    /// Nothing is set up unless <paramref name="configure"/> asks for it. Start
    /// from <see cref="NoeliaBuilder.UseDefaults"/> and depart from it with
    /// <c>Use</c> and <c>Without</c>:
    /// <code>
    /// builder.Services.AddNoelia(configuration, environment, "identity-service", noelia => noelia
    ///     .UseDefaults()
    ///     .Without(NoeliaModule.Communication, "no broker on this service")
    ///     .Use(NoeliaModule.TokenSessions));
    /// </code>
    /// <para>
    /// Calling it with an empty lambda registers nothing at all, and the service
    /// starts without any Noelia infrastructure. That is a legitimate thing to
    /// want and a visible thing to have done.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where module settings are read from.</param>
    /// <param name="environment">Decides the settings that differ per environment.</param>
    /// <param name="serviceName">This service's name, for logs and traces.</param>
    /// <param name="configure">Chooses the modules.</param>
    public static IServiceCollection AddNoelia(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName,
        Action<NoeliaBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var builder = new NoeliaBuilder(
            services, configuration, environment, serviceName,
            NoeliaModuleCatalogue.All, NoeliaModuleCatalogue.Defaults);

        configure(builder);
        builder.Build();

        return services;
    }
}
