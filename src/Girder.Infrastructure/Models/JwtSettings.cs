namespace Girder.Infrastructure.Models;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public string Secret { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;

    /// <summary>
    /// How long an access token is honoured, in minutes.
    /// </summary>
    /// <remarks>
    /// This is the window in which a signed-out person is still let in, unless
    /// a revocation store closes it — every service verifies the token from its
    /// signature alone and asks nothing. Fifteen minutes is the usual trade
    /// between that window and how often a client has to refresh.
    /// <para>
    /// It is not the refresh token's lifetime; that is
    /// <c>TokenSessions:RefreshTokenLifetime</c> and is measured in days.
    /// </para>
    /// </remarks>
    public int ExpireMinutes { get; set; } = 15;
}
