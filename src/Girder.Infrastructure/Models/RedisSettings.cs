namespace Infrastructure.Models;

public class RedisSettings
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string InstanceName { get; set; } = "skillswap";
    public int DefaultExpirationMinutes { get; set; } = 60;
    public bool EnableDistributedCache { get; set; } = true;
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
}