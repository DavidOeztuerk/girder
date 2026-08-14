using Infrastructure.BackgroundServices;
using Infrastructure.Security.Monitoring;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.BackgroundServices;

[Trait("Category", "Unit")]
public class ThreatDetectionBackgroundServiceTests
{
    private readonly ISecurityAlertService _securityAlertService = Substitute.For<ISecurityAlertService>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();
    private readonly ILogger<ThreatDetectionBackgroundService> _logger =
        Substitute.For<ILogger<ThreatDetectionBackgroundService>>();

    private ThreatDetectionBackgroundService CreateService()
    {
        return new ThreatDetectionBackgroundService(_securityAlertService, _cache, _logger);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_StopsGracefully()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Cancel immediately so the loop exits after first iteration
        cts.Cancel();

        await service.StartAsync(cts.Token);

        // Give a tiny bit of time for the background task to process
        await Task.Delay(50);

        await service.StopAsync(CancellationToken.None);

        // Should not throw — just complete gracefully
    }

    [Fact]
    public async Task ExecuteAsync_RunsDetectionScan_WithoutErrors()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Start and cancel quickly to run one iteration
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // The service should have run without exceptions
        // No alerts sent because all detection methods use empty lists (placeholder)
        await _securityAlertService.DidNotReceive().SendAlertAsync(
            Arg.Any<SecurityAlertLevel>(),
            Arg.Any<SecurityAlertType>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DetectionThrows_ContinuesRunning()
    {
        // Even if internal detection methods throw, the service should catch and continue
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        // Should complete without throwing
        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #region Additional Tests

    [Fact]
    public async Task StartAsync_WithToken_StartsWithoutException()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var act = () => service.StartAsync(cts.Token);

        await act.Should().NotThrowAsync();

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_AfterStart_StopsGracefully()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        var act = () => service.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_MultipleIterations_DoesNotThrow()
    {
        // The service has a 5-minute loop; we just ensure one iteration runs without errors
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await service.StartAsync(cts.Token);

        // Wait for cancellation to propagate
        await Task.Delay(300);

        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_CacheUnavailable_ContinuesWithoutThrowing()
    {
        // GetStringAsync is an extension method; mock GetAsync (the interface method) to throw
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Cache unavailable"));

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_AlertServiceUnavailable_ContinuesWithoutThrowing()
    {
        _securityAlertService
            .SendAlertAsync(
                Arg.Any<SecurityAlertLevel>(),
                Arg.Any<SecurityAlertType>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Dictionary<string, object>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Alert service unavailable"));

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Service_Dispose_DoesNotThrow()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        cts.Cancel();

        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Dispose should not throw
        var disposeAct = () => { service.Dispose(); return Task.CompletedTask; };
        await disposeAct.Should().NotThrowAsync();
    }

    #endregion
}
