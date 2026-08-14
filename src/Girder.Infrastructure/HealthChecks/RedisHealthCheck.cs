using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Diagnostics;

namespace Infrastructure.HealthChecks;

/// <summary>
/// Health check for Redis connectivity and basic operations
/// </summary>
public class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisHealthCheck> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var database = _connectionMultiplexer.GetDatabase();
            var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints().First());
            
            var stopwatch = Stopwatch.StartNew();
            
            // Test basic connectivity
            if (!_connectionMultiplexer.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis connection is not established");
            }

            // Test basic read/write operations
            var testKey = $"healthcheck:{Environment.MachineName}:{Guid.NewGuid()}";
            var testValue = DateTimeOffset.UtcNow.ToString();
            
            await database.StringSetAsync(testKey, testValue, TimeSpan.FromMinutes(1));
            var retrievedValue = await database.StringGetAsync(testKey);
            await database.KeyDeleteAsync(testKey);
            
            if (retrievedValue != testValue)
            {
                return HealthCheckResult.Unhealthy("Redis read/write test failed");
            }

            // Get server info
            var info = await server.InfoAsync("server");
            var memory = await server.InfoAsync("memory");
            
            stopwatch.Stop();

            var data = new Dictionary<string, object>
            {
                ["connection_test"] = "passed",
                ["read_write_test"] = "passed",
                ["response_time_ms"] = stopwatch.ElapsedMilliseconds,
                ["redis_version"] = info.SelectMany(g => g).FirstOrDefault(x => x.Key == "redis_version").Value ?? "unknown",
                ["used_memory"] = memory.SelectMany(g => g).FirstOrDefault(x => x.Key == "used_memory").Value ?? "unknown",
                ["connected_clients"] = info.SelectMany(g => g).FirstOrDefault(x => x.Key == "connected_clients").Value ?? "unknown"
            };

            if (stopwatch.ElapsedMilliseconds > 500)
            {
                _logger.LogWarning("Redis health check slow response: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                return HealthCheckResult.Degraded($"Redis responding slowly: {stopwatch.ElapsedMilliseconds}ms", data: data);
            }

            return HealthCheckResult.Healthy("Redis is healthy", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return HealthCheckResult.Unhealthy($"Redis health check failed: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Health check for Redis performance metrics
/// </summary>
public class RedisPerformanceHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisPerformanceHealthCheck> _logger;

    public RedisPerformanceHealthCheck(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisPerformanceHealthCheck> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var server = _connectionMultiplexer.GetServer(_connectionMultiplexer.GetEndPoints().First());
            var database = _connectionMultiplexer.GetDatabase();

            // Get performance metrics
            var info = await server.InfoAsync();
            var memory = info.SelectMany(g => g).Where(x => x.Key.StartsWith("used_memory")).ToDictionary(x => x.Key, x => x.Value);
            var stats = info.SelectMany(g => g).Where(x => x.Key.StartsWith("instantaneous_")).ToDictionary(x => x.Key, x => x.Value);
            
            // Test latency with multiple operations
            var latencyTests = new List<Task<TimeSpan>>();
            for (int i = 0; i < 10; i++)
            {
                latencyTests.Add(MeasureLatencyAsync(database));
            }

            var latencies = await Task.WhenAll(latencyTests);
            var averageLatency = latencies.Average(l => l.TotalMilliseconds);
            var maxLatency = latencies.Max(l => l.TotalMilliseconds);

            // Parse memory usage
            var usedMemory = long.TryParse(memory.GetValueOrDefault("used_memory", "0"), out var mem) ? mem : 0;
            var maxMemory = long.TryParse(memory.GetValueOrDefault("maxmemory", "0"), out var maxMem) ? maxMem : 0;
            
            var memoryUsagePercent = maxMemory > 0 ? (double)usedMemory / maxMemory * 100 : 0;

            // Parse connection metrics
            var connectedClients = int.TryParse(info.SelectMany(g => g).FirstOrDefault(x => x.Key == "connected_clients").Value, out var clients) ? clients : 0;
            var maxClients = int.TryParse(info.SelectMany(g => g).FirstOrDefault(x => x.Key == "maxclients").Value, out var maxCli) ? maxCli : 0;

            var data = new Dictionary<string, object>
            {
                ["average_latency_ms"] = averageLatency,
                ["max_latency_ms"] = maxLatency,
                ["used_memory_bytes"] = usedMemory,
                ["memory_usage_percent"] = memoryUsagePercent,
                ["connected_clients"] = connectedClients,
                ["max_clients"] = maxClients,
                ["ops_per_sec"] = stats.GetValueOrDefault("instantaneous_ops_per_sec", "0"),
                ["keyspace_hits"] = info.SelectMany(g => g).FirstOrDefault(x => x.Key == "keyspace_hits").Value ?? "0",
                ["keyspace_misses"] = info.SelectMany(g => g).FirstOrDefault(x => x.Key == "keyspace_misses").Value ?? "0"
            };

            // Determine health status based on metrics
            var issues = new List<string>();

            if (averageLatency > 100)
            {
                issues.Add($"High average latency: {averageLatency:F1}ms");
            }

            if (maxLatency > 1000)
            {
                issues.Add($"Very high max latency: {maxLatency:F1}ms");
            }

            if (memoryUsagePercent > 90)
            {
                issues.Add($"High memory usage: {memoryUsagePercent:F1}%");
            }

            if (connectedClients > maxClients * 0.9)
            {
                issues.Add($"High client connection usage: {connectedClients}/{maxClients}");
            }

            if (issues.Any())
            {
                var issueDescription = string.Join(", ", issues);
                _logger.LogWarning("Redis performance issues detected: {Issues}", issueDescription);
                return HealthCheckResult.Degraded($"Redis performance issues: {issueDescription}", data: data);
            }

            return HealthCheckResult.Healthy("Redis performance is good", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis performance health check failed");
            return HealthCheckResult.Degraded($"Redis performance check failed: {ex.Message}", ex);
        }
    }

    private async Task<TimeSpan> MeasureLatencyAsync(IDatabase database)
    {
        var stopwatch = Stopwatch.StartNew();
        await database.PingAsync();
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }
}