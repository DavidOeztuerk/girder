using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Extensions;

/// <summary>
/// The entry point.
/// </summary>
public static class GirderServiceCollectionExtensions
{
    /// <summary>
    /// Sets up the Girder modules this service asks for.
    /// </summary>
    /// <remarks>
    /// Nothing is set up unless <paramref name="configure"/> asks for it. Start
    /// from <see cref="GirderBuilder.UseDefaults"/> and depart from it with
    /// <c>Use</c> and <c>Without</c>:
    /// <code>
    /// builder.Services.AddGirder(configuration, environment, "identity-service", girder => girder
    ///     .UseDefaults()
    ///     .Without(GirderModule.Communication, "no broker on this service")
    ///     .Use(GirderModule.TokenSessions));
    /// </code>
    /// <para>
    /// Calling it with an empty lambda registers nothing at all, and the service
    /// starts without any Girder infrastructure. That is a legitimate thing to
    /// want and a visible thing to have done.
    /// </para>
    /// </remarks>
    /// <param name="services">The container.</param>
    /// <param name="configuration">Where module settings are read from.</param>
    /// <param name="environment">Decides the settings that differ per environment.</param>
    /// <param name="serviceName">This service's name, for logs and traces.</param>
    /// <param name="configure">Chooses the modules.</param>
    public static IServiceCollection AddGirder(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName,
        Action<GirderBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var builder = new GirderBuilder(
            services, configuration, environment, serviceName,
            GirderModuleCatalogue.All, GirderModuleCatalogue.Defaults);

        configure(builder);
        builder.Build();

        return services;
    }
}
