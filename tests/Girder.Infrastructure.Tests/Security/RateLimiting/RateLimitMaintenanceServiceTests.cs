using Girder.Infrastructure.Security.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.RateLimiting;

[Trait("Category", "Unit")]
public class RateLimitMaintenanceServiceTests
{
    private readonly IRateLimitService _rateLimitService = Substitute.For<IRateLimitService>();
    private readonly ILogger<RateLimitMaintenanceService> _logger =
        Substitute.For<ILogger<RateLimitMaintenanceService>>();

    private RateLimitMaintenanceService CreateService()
        => new RateLimitMaintenanceService(_rateLimitService, _logger);

    private void SetupDefaultStatistics(RateLimitStatistics? stats = null)
    {
        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats ?? new RateLimitStatistics());
        _rateLimitService.GetRegisteredRules().Returns(Enumerable.Empty<RateLimitRule>());
    }

    [Fact]
    public async Task StartAsync_WithPrecancelledToken_CompletesImmediately()
    {
        SetupDefaultStatistics();
        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task StopAsync_AfterStart_CompletesGracefully()
    {
        SetupDefaultStatistics();
        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_NoViolations_DoesNotBlacklist()
    {
        SetupDefaultStatistics(new RateLimitStatistics
        {
            TotalViolations = 0,
            TopViolatingClients = new Dictionary<string, long>()
        });

        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        await _rateLimitService.DidNotReceive().BlacklistClientAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ViolationsAboveBlacklistThreshold_AutoBlacklists()
    {
        // Threshold is 1000; put a client at 1001
        var stats = new RateLimitStatistics
        {
            TotalViolations = 1001,
            TopViolatingClients = new Dictionary<string, long>
            {
                ["bad-client"] = 1001L
            },
            ViolationsByRule = new Dictionary<string, long>()
        };
        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats);
        _rateLimitService.GetRegisteredRules().Returns(Enumerable.Empty<RateLimitRule>());

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Start service — ExecuteAsync runs in background with while(!cancelled) loop
        await service.StartAsync(cts.Token);

        // Wait for PerformMaintenanceTasks to execute, then cancel
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        await _rateLimitService.Received(1).BlacklistClientAsync(
            "bad-client",
            TimeSpan.FromHours(24),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ViolationsBelowBlacklistThreshold_DoesNotBlacklist()
    {
        var stats = new RateLimitStatistics
        {
            TotalViolations = 500,
            TopViolatingClients = new Dictionary<string, long>
            {
                ["medium-client"] = 500L
            },
            ViolationsByRule = new Dictionary<string, long>()
        };
        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats);
        _rateLimitService.GetRegisteredRules().Returns(Enumerable.Empty<RateLimitRule>());

        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        await _rateLimitService.DidNotReceive().BlacklistClientAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RuleViolationsAboveAdjustmentThreshold_AdjustsRule()
    {
        var ruleId = "rule-1";
        var rule = new RateLimitRule
        {
            Id = ruleId,
            Name = "Adjustable Rule",
            Configuration = new RateLimitConfiguration { RequestLimit = 1000 }
        };

        var stats = new RateLimitStatistics
        {
            TotalViolations = 0,
            TopViolatingClients = new Dictionary<string, long>(),
            ViolationsByRule = new Dictionary<string, long>
            {
                [ruleId] = 600L  // above RuleAdjustmentThreshold of 500
            }
        };

        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats);
        _rateLimitService.GetRegisteredRules().Returns(new[] { rule });

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Rule should be re-registered with reduced limit (1000 * 0.8 = 800)
        await _rateLimitService.Received(1).RegisterRuleAsync(
            Arg.Is<RateLimitRule>(r => r.Id == ruleId && r.Configuration.RequestLimit == 800),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RuleViolationsBelowAdjustmentThreshold_DoesNotAdjustRule()
    {
        var ruleId = "rule-2";
        var rule = new RateLimitRule
        {
            Id = ruleId,
            Name = "Stable Rule",
            Configuration = new RateLimitConfiguration { RequestLimit = 1000 }
        };

        var stats = new RateLimitStatistics
        {
            TotalViolations = 0,
            TopViolatingClients = new Dictionary<string, long>(),
            ViolationsByRule = new Dictionary<string, long>
            {
                [ruleId] = 400L  // below RuleAdjustmentThreshold of 500
            }
        };

        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats);
        _rateLimitService.GetRegisteredRules().Returns(new[] { rule });

        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

        await _rateLimitService.DidNotReceive().RegisterRuleAsync(
            Arg.Any<RateLimitRule>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_GetStatisticsThrows_ContinuesWithoutCrashing()
    {
        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Stats unavailable"));
        _rateLimitService.GetRegisteredRules().Returns(Enumerable.Empty<RateLimitRule>());

        var service = CreateService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Should not throw; outer catch logs the error
        var act = () => service.StartAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_MultipleViolatorsAboveThreshold_BlacklistsAll()
    {
        var stats = new RateLimitStatistics
        {
            TotalViolations = 5000,
            TopViolatingClients = new Dictionary<string, long>
            {
                ["client-a"] = 1500L,
                ["client-b"] = 2000L,
                ["client-c"] = 800L  // below threshold — should not be blacklisted
            },
            ViolationsByRule = new Dictionary<string, long>()
        };

        _rateLimitService.GetStatisticsAsync(
                Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(stats);
        _rateLimitService.GetRegisteredRules().Returns(Enumerable.Empty<RateLimitRule>());

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        await _rateLimitService.Received(1).BlacklistClientAsync(
            "client-a", Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _rateLimitService.Received(1).BlacklistClientAsync(
            "client-b", Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _rateLimitService.DidNotReceive().BlacklistClientAsync(
            "client-c", Arg.Any<TimeSpan>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Constructor_CreatesServiceWithoutThrowing()
    {
        var act = () => new RateLimitMaintenanceService(_rateLimitService, _logger);
        act.Should().NotThrow();
    }
}
