using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Girder.Infrastructure.Security.Keys;

/// <summary>
/// A key a service holds in order to issue tokens, verify them, or both.
/// </summary>
/// <remarks>
/// The distinction is the point. With a shared secret the verification key
/// <b>is</b> the signing key, so every service that checks a token can also
/// mint one — for any subject, with any role. An asymmetric pair lets the
/// issuing service hold the private half and everyone else hold the public
/// half, and <see cref="CanSign"/> makes that a property of the object rather
/// than a rule someone has to remember.
/// <para>
/// ES256 over RS256 by default: a P-256 key is a single short base64 line in an
/// environment variable, where an RSA key is a multi-line PEM — and signing
/// happens on every sign-in and every refresh, where ECDSA is the cheaper half.
/// RS256 stays available because some identity providers offer nothing else.
/// </para>
/// </remarks>
public sealed class SigningKey
{
    private readonly AsymmetricSecurityKey? _asymmetric;
    private readonly SymmetricSecurityKey? _symmetric;

    private SigningKey(
        SecurityKey key,
        string algorithm,
        string? kid,
        bool canSign,
        bool separates)
    {
        _asymmetric = key as AsymmetricSecurityKey;
        _symmetric = key as SymmetricSecurityKey;
        Key = key;
        Algorithm = algorithm;
        Kid = kid;
        CanSign = canSign;
        SeparatesIssuingFromVerifying = separates;

        if (kid is not null)
        {
            key.KeyId = kid;
        }
    }

    /// <summary>The underlying key, for the token handler.</summary>
    internal SecurityKey Key { get; }

    /// <summary>
    /// The one algorithm this key may be used with.
    /// </summary>
    /// <remarks>
    /// Derived from the key material, never from a token header. Choosing the
    /// algorithm from the header is what lets an attacker present a token
    /// marked <c>HS256</c> and signed with the public key as its secret.
    /// </remarks>
    public string Algorithm { get; }

    /// <summary>
    /// Identifies this key in the <c>kid</c> header, so a rotation can overlap
    /// instead of invalidating every token in circulation at once. Null for a
    /// shared secret that was never given one.
    /// </summary>
    public string? Kid { get; }

    /// <summary>Whether this key can produce a signature.</summary>
    public bool CanSign { get; }

    /// <summary>
    /// Whether holding this key for verification leaves the holder unable to
    /// issue. False for a shared secret, and that is the whole reason to prefer
    /// a pair.
    /// </summary>
    public bool SeparatesIssuingFromVerifying { get; }

    /// <summary>Credentials for signing.</summary>
    /// <exception cref="InvalidOperationException">This key cannot sign.</exception>
    public SigningCredentials SigningCredentials() =>
        CanSign
            ? new SigningCredentials(Key, Algorithm)
            : throw new InvalidOperationException(
                "This is a public key and cannot sign. A service that only verifies tokens "
                + "holds the public half; the issuing service holds the private one.");

    /// <summary>Reads a P-256 private key from base64-encoded PKCS#8.</summary>
    /// <param name="base64Pkcs8">The key, as one base64 line.</param>
    /// <param name="kid">Identifier written into the token header.</param>
    public static SigningKey FromEcdsaPrivateKey(string base64Pkcs8, string kid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Pkcs8);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(base64Pkcs8), out _);

        return new SigningKey(
            new ECDsaSecurityKey(ecdsa),
            SecurityAlgorithms.EcdsaSha256,
            kid,
            canSign: true,
            separates: true);
    }

    /// <summary>Reads a P-256 public key from base64-encoded SubjectPublicKeyInfo.</summary>
    /// <param name="base64Spki">The key, as one base64 line.</param>
    /// <param name="kid">Identifier the token header must carry to select this key.</param>
    public static SigningKey FromEcdsaPublicKey(string base64Spki, string kid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Spki);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64Spki), out _);

        return new SigningKey(
            new ECDsaSecurityKey(ecdsa),
            SecurityAlgorithms.EcdsaSha256,
            kid,
            canSign: false,
            separates: true);
    }

    /// <summary>
    /// A shared secret, signing and verifying with the same material.
    /// </summary>
    /// <remarks>
    /// Kept for services already issuing HS256 tokens and for the window in
    /// which both algorithms are in circulation. It cannot separate issuing
    /// from verifying — every holder can mint.
    /// </remarks>
    /// <param name="secret">At least 32 characters.</param>
    /// <param name="kid">Usually null: tokens issued before rotation carry none.</param>
    public static SigningKey FromSharedSecret(string secret, string? kid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        if (secret.Length < 32)
        {
            throw new ArgumentException("A shared secret must be at least 32 characters.", nameof(secret));
        }

        return new SigningKey(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret)),
            SecurityAlgorithms.HmacSha256,
            kid,
            canSign: true,
            separates: false);
    }

    /// <summary>
    /// Derives a P-256 pair deterministically from <paramref name="seed"/>, so
    /// several services on one machine verify the same signatures without
    /// anyone generating and distributing a key first.
    /// </summary>
    /// <remarks>
    /// Provides <b>no</b> separation: every service holding the seed can issue.
    /// That is acceptable while everything runs on one developer's machine and
    /// nowhere else, which is why this refuses outside development.
    /// </remarks>
    /// <param name="seed">Any string; the same seed yields the same key.</param>
    /// <param name="environment">Checked, so the seed cannot reach a deployment.</param>
    /// <exception cref="InvalidOperationException">Not a development environment.</exception>
    public static SigningKey DevelopmentFromSeed(string seed, IHostEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        ArgumentNullException.ThrowIfNull(environment);

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"DevelopmentFromSeed refuses to run in {environment.EnvironmentName}. "
                + "It gives every service that holds the seed the power to issue tokens. "
                + "Generate a key pair and give the private half to the issuing service only.");
        }

        return FromEcdsaPrivateKey(Convert.ToBase64String(DeriveP256(seed)), kid: "development");
    }

    /// <summary>
    /// Turns a seed into a valid P-256 private key by rejection sampling.
    /// </summary>
    /// <remarks>
    /// A hash is a 256-bit number, not necessarily a valid scalar: it has to be
    /// in [1, n-1]. Counting up through the hash input until the curve accepts
    /// one keeps the result deterministic.
    /// </remarks>
    private static byte[] DeriveP256(string seed)
    {
        for (var counter = 0; counter < 1000; counter++)
        {
            var candidate = SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes($"girder-dev-key:{counter}:{seed}"));

            try
            {
                using var ecdsa = ECDsa.Create(new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    D = candidate,
                    Q = DerivePublicPoint(candidate)
                });

                return ecdsa.ExportPkcs8PrivateKey();
            }
            catch (CryptographicException)
            {
                // Not a valid scalar for this curve; try the next counter.
            }
        }

        throw new InvalidOperationException("Could not derive a key from this seed.");
    }

    /// <summary>
    /// Computes the public point for a private scalar.
    /// </summary>
    /// <remarks>
    /// .NET exposes no scalar multiplication, so the point comes back out of a
    /// key built from the scalar alone — which the platform allows for a named
    /// curve, and which is why the import above needs the result rather than
    /// computing it itself.
    /// </remarks>
    private static ECPoint DerivePublicPoint(byte[] scalar)
    {
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = scalar
        });

        return ecdsa.ExportParameters(includePrivateParameters: false).Q;
    }
}
