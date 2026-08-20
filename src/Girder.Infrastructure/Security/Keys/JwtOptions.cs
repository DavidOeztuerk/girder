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
}
