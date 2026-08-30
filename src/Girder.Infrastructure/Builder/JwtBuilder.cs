using Girder.Abstractions.Hosting;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Keys;

namespace Girder.Infrastructure.Builder;

/// <summary>
/// Which keys this service accepts, and whether it may mint tokens of its own.
/// </summary>
/// <remarks>
/// The distinction the shape is for: a service that only reads tokens should not
/// hold a key that can write them. With a shared secret it cannot tell the two
/// apart — every holder of the secret can mint a token for any subject, so one
/// leaked configuration file is every account on every service.
/// </remarks>
public sealed class JwtBuilder
{
    private readonly GirderBuilder _girder;
    private readonly List<SigningKey> _verify = [];
    private SigningKey? _sign;
    private string? _authority;
    private bool _decided;

    internal JwtBuilder(GirderBuilder girder) => _girder = girder;

    /// <summary>
    /// Reads tokens signed elsewhere, and cannot write one.
    /// </summary>
    /// <remarks>
    /// What every service except the one that signs in should do. The public key
    /// verifies and nothing else, so a copy of this service's configuration is
    /// worth nothing to an attacker.
    /// </remarks>
    /// <param name="publicKey">The issuer's public key, base64 SPKI.</param>
    /// <param name="keyId">
    /// Names the key, so the issuer can rotate: tokens carry the id, and a
    /// service that knows both keys accepts both while the change goes through.
    /// </param>
    public JwtBuilder VerifyOnly(string publicKey, string keyId)
    {
        Decide();
        _verify.Add(SigningKey.FromEcdsaPublicKey(publicKey, keyId));
        return this;
    }

    /// <summary>
    /// Also accepts a second public key, for the span of a rotation.
    /// </summary>
    public JwtBuilder AlsoVerify(string publicKey, string keyId)
    {
        _verify.Add(SigningKey.FromEcdsaPublicKey(publicKey, keyId));
        return this;
    }

    /// <summary>
    /// Signs tokens with this key, and verifies with it.
    /// </summary>
    /// <remarks>
    /// For the one service that signs people in. Every other service should get
    /// the matching public key through <see cref="VerifyOnly"/>.
    /// </remarks>
    /// <param name="privateKey">Base64 PKCS#8.</param>
    /// <param name="keyId">Names the key in the tokens it signs.</param>
    public JwtBuilder Issue(string privateKey, string keyId)
    {
        Decide();
        _sign = SigningKey.FromEcdsaPrivateKey(privateKey, keyId);
        _verify.Add(_sign);
        return this;
    }

    /// <summary>
    /// Takes the keys from an OpenID Connect provider.
    /// </summary>
    /// <remarks>
    /// The provider publishes its keys and rotates them, and discovery follows
    /// both — which is why no key is configured here.
    /// </remarks>
    public JwtBuilder From(string authority)
    {
        Decide();
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        _authority = authority;
        return this;
    }

    /// <summary>
    /// Takes one shared secret from configuration, and can both sign and verify
    /// with it.
    /// </summary>
    /// <remarks>
    /// The weakest arrangement Girder supports, and the one to leave behind
    /// first: every service holding the secret can mint a token for any subject,
    /// so there is no such thing as a read-only service. Reads <c>JWT_SECRET</c>
    /// or <c>JwtSettings:Secret</c>, and refuses a placeholder value.
    /// </remarks>
    public JwtBuilder FromSharedSecret()
    {
        Decide();
        _girder.Services.AddJwtAuthentication(_girder.Configuration, _girder.Environment);
        return this;
    }

    internal void Apply()
    {
        if (!_decided)
        {
            throw new InvalidOperationException(
                "UseJwt needs to say where the keys come from: VerifyOnly(...) for a service "
                + "that only reads tokens, Issue(...) for the one that signs them, From(authority) "
                + "for an OpenID Connect provider, or FromSharedSecret() for one secret shared by all.");
        }

        if (_verify.Count == 0 && _authority is null)
        {
            // FromSharedSecret registered the scheme itself.
            return;
        }

        _girder.Services.AddJwtAuthentication(
            _verify.Count == 0 ? null : new KeyRing(_verify, _sign),
            _authority,
            _girder.Configuration,
            _girder.Environment);
    }

    private void Decide()
    {
        if (_decided)
        {
            throw new InvalidOperationException(
                "The token keys were already settled. Use AlsoVerify(...) to accept a second "
                + "key during a rotation; naming a second source would leave it unclear which wins.");
        }

        _decided = true;
    }
}

/// <summary>Token verification, with its keys.</summary>
public static class JwtModuleExtensions
{
    /// <summary>
    /// Sets up the bearer scheme and says where its keys come from.
    /// </summary>
    /// <remarks>
    /// Without this call the service registers <c>IJwtService</c> — it can read
    /// and write tokens in code — but no authentication scheme, so
    /// <c>[Authorize]</c> has nothing to authenticate with and every endpoint
    /// behind it answers 500 rather than 401.
    /// </remarks>
    public static GirderBuilder UseJwt(this GirderBuilder girder, Action<JwtBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(girder);
        ArgumentNullException.ThrowIfNull(configure);

        girder.Use(GirderModule.Jwt);

        var jwt = new JwtBuilder(girder);
        configure(jwt);
        jwt.Apply();

        return girder;
    }
}
