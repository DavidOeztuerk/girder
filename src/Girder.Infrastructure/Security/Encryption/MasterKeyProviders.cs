using Girder.Infrastructure.Security.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security.Encryption;

/// <summary>
/// Reads the master key from configuration, expecting base64.
/// </summary>
/// <remarks>
/// Suitable where the key arrives as an environment variable from a secret
/// mechanism outside the application — a Kubernetes secret, a systemd
/// credential. The key is then in the process environment, which is a weaker
/// position than <see cref="SecretStoreMasterKeyProvider"/> but an honest one.
/// </remarks>
public sealed class ConfiguredMasterKeyProvider : IMasterKeyProvider
{
    public const string ConfigurationKey = "Encryption:MasterKey";

    private readonly byte[] _masterKey;

    public ConfiguredMasterKeyProvider(IConfiguration configuration)
    {
        var configured = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is not set. Girder does not generate a master key, "
                + "because a key the application invents is a key the operator does not hold.");
        }

        _masterKey = MasterKey.Parse(configured, ConfigurationKey);
    }

    /// <inheritdoc />
    public byte[] GetMasterKey() => _masterKey;
}

/// <summary>
/// Reads the master key from the configured secret store.
/// </summary>
/// <remarks>
/// The sovereign arrangement: the key lives in a store you run — OpenBao, for
/// instance — and never appears in configuration, in an image, or in a backup
/// of this application. Resolution is deferred until first use and then cached,
/// so a store that is unreachable fails the first operation rather than the
/// process start.
/// </remarks>
public sealed class SecretStoreMasterKeyProvider : IMasterKeyProvider
{
    public const string DefaultSecretName = "girder/master-key";

    private readonly Lazy<byte[]> _masterKey;

    public SecretStoreMasterKeyProvider(
        ISecretProvider secretProvider,
        ILogger<SecretStoreMasterKeyProvider> logger,
        string? secretName = null)
    {
        var name = secretName ?? DefaultSecretName;

        _masterKey = new Lazy<byte[]>(() =>
        {
            var value = secretProvider.GetSecretAsync(name).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"The secret store holds no value for '{name}'. Create it there; "
                    + "Girder will not generate one.");
            }

            logger.LogInformation("Master key resolved from the secret store");

            return MasterKey.Parse(value, name);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public byte[] GetMasterKey() => _masterKey.Value;
}

internal static class MasterKey
{
    public const int SizeBytes = 32;

    public static byte[] Parse(string base64, string source)
    {
        byte[] key;

        try
        {
            key = Convert.FromBase64String(base64.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"The master key in '{source}' is not valid base64.");
        }

        if (key.Length != SizeBytes)
        {
            throw new InvalidOperationException(
                $"The master key in '{source}' is {key.Length} bytes; {SizeBytes} are required.");
        }

        return key;
    }
}

public static class MasterKeyProviderExtensions
{
    /// <summary>
    /// Takes the master key from <c>Encryption:MasterKey</c>.
    /// </summary>
    public static IServiceCollection AddConfiguredMasterKey(this IServiceCollection services) =>
        services.AddSingleton<IMasterKeyProvider, ConfiguredMasterKeyProvider>();

    /// <summary>
    /// Takes the master key from the registered <see cref="ISecretProvider"/>.
    /// </summary>
    public static IServiceCollection AddSecretStoreMasterKey(
        this IServiceCollection services,
        string? secretName = null) =>
        services.AddSingleton<IMasterKeyProvider>(sp => new SecretStoreMasterKeyProvider(
            sp.GetRequiredService<ISecretProvider>(),
            sp.GetRequiredService<ILogger<SecretStoreMasterKeyProvider>>(),
            secretName));
}
