namespace Infrastructure.Models;

public class EndpointRateLimit
{
    public string Path { get; set; } = string.Empty;
    public int RequestsPerMinute { get; set; }
    public int RequestsPerHour { get; set; }
    public int RequestsPerDay { get; set; }
}
