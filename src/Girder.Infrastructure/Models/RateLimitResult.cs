namespace Infrastructure.Models;

public class RateLimitResult
{
    public string ClientId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public bool IsAllowed { get; set; } = true;
    public int RequestsPerMinute { get; set; }
    public int RequestsPerHour { get; set; }
    public int RequestsPerDay { get; set; }
    public int LimitsPerMinute { get; set; }
    public int LimitsPerHour { get; set; }
    public int LimitsPerDay { get; set; }
}
