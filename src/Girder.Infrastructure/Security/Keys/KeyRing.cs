using Microsoft.IdentityModel.Tokens;

namespace Girder.Infrastructure.Security.Keys;

/// <summary>
/// The keys a service verifies with, and the one it issues with — if it issues
/// at all.
/// </summary>
/// <remarks>
/// A list rather than a single key, because both rotation and migration need
/// two valid keys at once: a new <c>kid</c> starts issuing while tokens under
/// the previous one are still in circulation, and a move from a shared secret
/// to a key pair has a window where both algorithms are live. With a single
/// key, either change signs every user out at the moment it takes effect.
/// </remarks>
public sealed class KeyRing
{
    private readonly IReadOnlyList<SigningKey> _validationKeys;

    /// <param name="validationKeys">Every key whose signatures are still honoured.</param>
    /// <param name="signingKey">
    /// The key new tokens are signed with, or <c>null</c> for a service that
    /// only verifies. Null is the ordinary case: most services consume tokens
    /// and must not be able to mint them.
    /// </param>
    /// <exception cref="ArgumentException">
    /// No validation keys, or a signing key that cannot sign.
    /// </exception>
    public KeyRing(IReadOnlyList<SigningKey> validationKeys, SigningKey? signingKey)
    {
        ArgumentNullException.ThrowIfNull(validationKeys);

        if (validationKeys.Count == 0)
        {
            throw new ArgumentException(
                "A service needs at least one key to verify with.", nameof(validationKeys));
        }

        if (signingKey is { CanSign: false })
        {
            throw new ArgumentException(
                "The signing key cannot sign. Give the issuing service the private half.",
                nameof(signingKey));
        }

        _validationKeys = validationKeys;
        SigningKey = signingKey;
    }

    /// <summary>The key new tokens are signed with, or null for a verifier.</summary>
    public SigningKey? SigningKey { get; }

    /// <summary>Whether this service can issue tokens at all.</summary>
    public bool CanIssue => SigningKey is not null;

    /// <summary>
    /// Validation parameters that accept exactly the configured keys and
    /// exactly their algorithms.
    /// </summary>
    /// <remarks>
    /// A token marked <c>HS256</c> and signed with the public key as its HMAC
    /// secret is refused because the handler never treats an asymmetric key as
    /// symmetric material — the protection comes from key-type matching, not
    /// from the algorithm list. <see cref="TokenValidationParameters.ValidAlgorithms"/>
    /// is pinned anyway, so the accepted set is a decision of this ring rather
    /// than a behaviour of whichever version of the token library is installed.
    /// <para>
    /// <c>kid</c> selects which key, never which algorithm — the algorithm
    /// follows from the key material that was configured.
    /// </para>
    /// </remarks>
    /// <param name="issuer">Expected <c>iss</c>.</param>
    /// <param name="audience">Expected <c>aud</c>.</param>
    /// <param name="validateLifetime">
    /// False only where an expired token is the input by design — reading the
    /// claims of a token in order to refresh it. Keys, algorithms, issuer and
    /// audience are checked in both cases; expiry is the single axis that
    /// differs, and it is named rather than left to a second copy of these
    /// parameters that would drift.
    /// </param>
    public TokenValidationParameters ValidationParameters(
        string issuer,
        string audience,
        bool validateLifetime = true) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = validateLifetime,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKeys = _validationKeys.Select(key => key.Key).ToArray(),
        ValidAlgorithms = _validationKeys.Select(key => key.Algorithm).Distinct().ToArray(),
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ClockSkew = TimeSpan.Zero
    };

    /// <summary>The signing key, for code that must issue.</summary>
    /// <exception cref="InvalidOperationException">This service only verifies.</exception>
    public SigningKey RequireSigningKey() =>
        SigningKey ?? throw new InvalidOperationException(
            "This service holds no signing key and cannot issue tokens. That is the "
            + "ordinary configuration for a service that only verifies them.");
}
