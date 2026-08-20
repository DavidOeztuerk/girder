namespace Girder.Infrastructure.Security.Keys;

/// <summary>
/// Which keys a service signs and verifies with.
/// </summary>
/// <remarks>
/// Leave both empty and Girder falls back to the shared secret from
/// <c>JwtSettings:Secret</c> or <c>JWT_SECRET</c> — the configuration every
/// existing service is on. That path signs and verifies with the same
/// material, so every holder can issue; a service that only consumes tokens
/// should be given <see cref="ValidationKeys"/> alone instead.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>
    /// The key new tokens are signed with. Null for a service that only
    /// verifies — which is most of them.
    /// </summary>
    public SigningKey? SigningKey { get; set; }

    /// <summary>
    /// Every key whose signatures are still honoured.
    /// </summary>
    /// <remarks>
    /// More than one during a rotation or a move off a shared secret: tokens
    /// under the previous key stay valid while the new one issues. With a
    /// single entry, either change signs every user out at once.
    /// </remarks>
    public IList<SigningKey> ValidationKeys { get; } = [];

    /// <summary>
    /// An OpenID Connect provider whose published keys verify the tokens —
    /// Keycloak, Zitadel, authentik, or any other.
    /// </summary>
    /// <remarks>
    /// Set it and the bearer scheme discovers the keys and follows their
    /// rotation; no local key is needed and none should be configured for
    /// verification. That is the whole point: a library whose own sign-in
    /// cannot be exchanged for a provider is itself the dependency it claims
    /// to prevent.
    /// <para>
    /// A service behind a provider does not issue tokens, so no
    /// <c>IJwtService</c> is registered unless a
    /// <see cref="SigningKey"/> is also given — which is the shape a migration
    /// has, where both are live for a while.
    /// </para>
    /// </remarks>
    public string? Authority { get; set; }
}
