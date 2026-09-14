using Girder.Abstractions.Security.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security.Secrets;

/// <summary>
/// Registers concrete secret stores behind the provider port.
/// </summary>
public static class SecretProviderRegistration
{
    private const string OpenBaoClientName = "Girder.OpenBaoSecretProvider";

    /// <summary>
    /// Uses a self-hosted OpenBao KV-v2 store, or another server speaking the
    /// same API, as the application's <see cref="ISecretProvider"/>.
    /// </summary>
    /// <remarks>
    /// Reads <c>OpenBao:Address</c>, <c>OpenBao:Token</c>,
    /// <c>OpenBao:MountPoint</c> and the optional
    /// <c>OpenBao:Namespace</c>. The older <c>Vault:*</c> names remain accepted
    /// for compatible deployments.
    /// </remarks>
    public static IServiceCollection AddOpenBaoSecretProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient(OpenBaoClientName);
        services.AddSingleton(provider => new OpenBaoSecretProvider(
            provider.GetRequiredService<ILogger<OpenBaoSecretProvider>>(),
            configuration,
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(OpenBaoClientName)));
        services.AddSingleton<ISecretProvider>(
            provider => provider.GetRequiredService<OpenBaoSecretProvider>());
        services.AddSingleton<IVersionedSecretProvider>(
            provider => provider.GetRequiredService<OpenBaoSecretProvider>());

        return services;
    }
}
