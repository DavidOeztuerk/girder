namespace Girder.Infrastructure.Security.Sessions;

/// <summary>How long a sign-in lasts, and how forgiving refreshing is.</summary>
/// <remarks>
/// None of these is the access token's lifetime — that is
/// <c>JwtSettings:ExpireMinutes</c> and belongs to the token every service
/// verifies on its own. These govern the refresh token, which only the issuing
/// service ever sees.
/// </remarks>
public sealed class TokenSessionOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "TokenSessions";

    /// <summary>
    /// How long one refresh token is valid. Each rotation issues a new one, so
    /// this is really "how long may a client be away and still come back".
    /// </summary>
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>
    /// The ceiling for a whole sign-in, however often it is refreshed.
    /// </summary>
    /// <remarks>
    /// Without it, rotation plus a sliding lifetime is a session that never
    /// ends — which is exactly the state an undetected stolen token wants.
    /// </remarks>
    public TimeSpan AbsoluteSessionLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long after a rotation a second presentation of the old token counts
    /// as a race rather than a theft.
    /// </summary>
    /// <remarks>
    /// Two tabs both hitting a 401, or a client retrying, present the same
    /// token twice within moments. Treating that as theft signs out people who
    /// did nothing wrong, and a library that does it is one nobody keeps.
    /// <para>
    /// It is a deliberate, bounded concession: inside this window a replay by
    /// an attacker cannot be told from a second tab. Seconds, never minutes.
    /// </para>
    /// </remarks>
    public TimeSpan ReuseGracePeriod { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How many unconsumed refresh tokens one sign-in may hold.
    /// </summary>
    /// <remarks>
    /// Every rotation inside the grace window adds a sibling. A client retry
    /// loop would otherwise grow one session without limit; on overflow the
    /// oldest sibling is dropped.
    /// </remarks>
    public int MaxConcurrentTokensPerSession { get; set; } = 5;
}
