using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System.Diagnostics;

namespace Infrastructure.HealthChecks;

public class RabbitMQHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;
    private readonly ILogger<RabbitMQHealthCheck> _logger;

    public RabbitMQHealthCheck(IConnection connection, ILogger<RabbitMQHealthCheck> logger)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            if (!_connection.IsOpen)
            {
                _logger.LogWarning("RabbitMQ connection is closed");
                return HealthCheckResult.Unhealthy("RabbitMQ connection is closed");
            }

            using var channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            if (!channel.IsOpen)
            {
                _logger.LogWarning("RabbitMQ channel could not be opened");
                return HealthCheckResult.Unhealthy("RabbitMQ channel could not be opened");
            }

            stopwatch.Stop();
            
            var data = new Dictionary<string, object>
            {
                ["responseTime"] = $"{stopwatch.ElapsedMilliseconds}ms",
                ["isOpen"] = _connection.IsOpen,
                ["channelOpen"] = channel.IsOpen
            };

            _logger.LogDebug("RabbitMQ health check completed in {ElapsedMilliseconds}ms", 
                stopwatch.ElapsedMilliseconds);

            return HealthCheckResult.Healthy("RabbitMQ is accessible", data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ health check failed");
            return HealthCheckResult.Unhealthy("RabbitMQ check failed", ex);
        }
    }
}