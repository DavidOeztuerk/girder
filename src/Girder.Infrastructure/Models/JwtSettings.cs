namespace Infrastructure.Models;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public string Secret { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;

    /// <summary>
    /// Token expiration time in minutes. Default is 60 minutes (1 hour).
    /// </summary>
    public int ExpireMinutes { get; set; } = 60;  // Default to 60 minutes
}
