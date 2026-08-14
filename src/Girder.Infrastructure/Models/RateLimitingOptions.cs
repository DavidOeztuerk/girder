namespace Infrastructure.Models;

/// <summary>
/// Rate limiting configuration
/// </summary>
public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int RequestsPerMinute { get; set; } = 100;
    public int RequestsPerHour { get; set; } = 10000;
    public int RequestsPerDay { get; set; } = 100000;
    public bool EnableIpRateLimiting { get; set; } = true;
    public bool EnableUserRateLimiting { get; set; } = true;
    public List<string> WhitelistedIps { get; set; } = new();
    public List<string> WhitelistedUserIds { get; set; } = new();
    public Dictionary<string, EndpointRateLimit> EndpointSpecificLimits { get; set; } = new();
}