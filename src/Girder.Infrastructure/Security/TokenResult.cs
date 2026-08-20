namespace Girder.Infrastructure.Security;

/// <summary>An issued access token.</summary>
/// <remarks>
/// Carries no refresh token. It used to, filled with random bytes that were
/// stored nowhere and validated by nothing — an API that looked like a feature
/// and was a hole for anyone who trusted it. Refresh tokens are issued by
/// <c>ITokenSessionService</c>, which also records them.
/// </remarks>
public class TokenResult
{
    public string AccessToken { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public string TokenType { get; set; } = "Bearer";

    public int ExpiresIn => (int)(ExpiresAt - DateTime.UtcNow).TotalSeconds;
}
