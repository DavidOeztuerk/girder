using Girder.Core.Exceptions;
using Microsoft.Extensions.Configuration;

namespace Girder.Infrastructure.Security.Keys;

/// <summary>
/// Builds the <see cref="KeyRing"/> from explicit keys, or from the shared
/// secret when none were given.
/// </summary>
internal static class KeyRingFactory
{
    /// <summary>
    /// The ring for this service, or <c>null</c> when an external provider
    /// supplies the keys and this service signs nothing.
    /// </summary>
    internal static KeyRing? Build(JwtOptions options, IConfiguration configuration)
    {
        if (options.ValidationKeys.Count > 0 || options.SigningKey is not null)
        {
            // A configured signing key is always honoured too, or an issuer
            // could not verify the tokens it just handed out.
            var validation = options.ValidationKeys.ToList();
            if (options.SigningKey is { } signing && !validation.Contains(signing))
            {
                validation.Add(signing);
            }

            return new KeyRing(validation, options.SigningKey);
        }

        // Behind a provider there is nothing local to build from, and
        // demanding a secret would make the exchange impossible.
        if (options.Authority is not null)
        {
            return null;
        }

        var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
            ?? configuration["JwtSettings:Secret"]
            ?? throw new ConfigurationException("JWT_SECRET", "JwtSettings",
                "No signing key configured. Pass keys to AddJwtAuthentication(o => ...), "
                + "or set JwtSettings:Secret for the shared-secret path.");

        var shared = SigningKey.FromSharedSecret(secret, kid: null);
        return new KeyRing([shared], shared);
    }
}
