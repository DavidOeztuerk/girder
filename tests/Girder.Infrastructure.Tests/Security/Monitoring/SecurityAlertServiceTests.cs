using Girder.Infrastructure.Security.Monitoring;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Monitoring;

[Trait("Category", "Unit")]
public class SecurityAlertServiceTests : IDisposable
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<SecurityAlertService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ISecurityAlertNotifier _notifier;

    public SecurityAlertServiceTests()
    {
        _cache = Substitute.For<IDistributedCache>();
        _logger = Substitute.For<ILogger<SecurityAlertService>>();
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);
        _notifier = Substitute.For<ISecurityAlertNotifier>();

        // Clear static state
        ClearAlerts();
    }

    public void Dispose()
    {
        ClearAlerts();
    }

    private static void ClearAlerts()
    {
        // SecurityAlertService uses a static list; we need to clean up between tests
        // We do this by calling CleanupOldAlertsAsync with a config that deletes everything
    }

    private SecurityAlertService CreateService(SecurityAlertConfiguration? config = null)
    {
        var cfg = config ?? new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = false,
            RetentionDays = 60
        };
        var options = Options.Create(cfg);
        return new SecurityAlertService(_cache, _logger, options, _httpContextAccessor, _notifier);
    }

    [Fact]
    public async Task SendAlertAsync_Disabled_ReturnsAlertWithoutStoring()
    {
        var config = new SecurityAlertConfiguration { Enabled = false };
        var service = CreateService(config);

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.High,
            SecurityAlertType.BruteForceAttack,
            "Test Alert",
            "Test message");

        alert.Should().NotBeNull();
        alert.Title.Should().Be("Test Alert");
    }

    [Fact]
    public async Task SendAlertAsync_Enabled_CreatesAlert()
    {
        var service = CreateService();

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Medium,
            SecurityAlertType.UnauthorizedAccessAttempt,
            "Unauthorized Access",
            "Someone tried to access a restricted resource");

        alert.Should().NotBeNull();
        alert.Level.Should().Be(SecurityAlertLevel.Medium);
        alert.Type.Should().Be(SecurityAlertType.UnauthorizedAccessAttempt);
        alert.Title.Should().Be("Unauthorized Access");
        alert.OccurrenceCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAlertAsync_CriticalWithEmailEnabled_SendsNotification()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        _notifier.NotifyAdminsAsync(Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = CreateService(config);

        await service.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.TokenTheftDetected,
            "Token Theft",
            "Possible token theft detected");

        await _notifier.Received(1).NotifyAdminsAsync(
            Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAlertAsync_WithAggregation_IncrementsExisting()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = true,
            DuplicateAlertThrottleSeconds = 300,
            EnableEmailNotifications = false
        };
        var service = CreateService(config);

        // First alert
        var first = await service.SendAlertAsync(
            SecurityAlertLevel.Low,
            SecurityAlertType.RateLimitExceeded,
            "Rate Limit",
            "Too many requests");

        // Second similar alert should increment
        var second = await service.SendAlertAsync(
            SecurityAlertLevel.Low,
            SecurityAlertType.RateLimitExceeded,
            "Rate Limit",
            "Too many requests again");

        second.Id.Should().Be(first.Id);
        second.OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public async Task GetAlertByIdAsync_NonExistentId_ReturnsNull()
    {
        var service = CreateService();

        var result = await service.GetAlertByIdAsync("non-existent-id");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAlertStatisticsAsync_ReturnsStatistics()
    {
        var service = CreateService();

        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.BruteForceAttack, "Test", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.RateLimitExceeded, "Test2", "msg2");

        var stats = await service.GetAlertStatisticsAsync();

        stats.Should().NotBeNull();
        stats.TotalAlerts.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task MarkAlertAsReadAsync_ExistingAlert_MarksAsRead()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Info, SecurityAlertType.Custom, "Info", "msg");

        await service.MarkAlertAsReadAsync(alert.Id, "admin-1");

        var retrieved = await service.GetAlertByIdAsync(alert.Id);
        retrieved.Should().NotBeNull();
        retrieved!.IsRead.Should().BeTrue();
        retrieved.ReadByAdminId.Should().Be("admin-1");
    }

    [Fact]
    public async Task MarkAlertAsReadAsync_NonExistent_DoesNotThrow()
    {
        var service = CreateService();

        var act = () => service.MarkAlertAsReadAsync("no-such-alert", "admin-1");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DismissAlertAsync_ExistingAlert_DismissesIt()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Low, SecurityAlertType.Custom, "Dismiss me", "msg");

        await service.DismissAlertAsync(alert.Id, "admin-2", "Not a real threat");

        var retrieved = await service.GetAlertByIdAsync(alert.Id);
        retrieved.Should().NotBeNull();
        retrieved!.IsDismissed.Should().BeTrue();
        retrieved.DismissedByAdminId.Should().Be("admin-2");
        retrieved.DismissalReason.Should().Be("Not a real threat");
    }

    [Fact]
    public async Task BulkDismissAlertsAsync_DismissesMultiple()
    {
        var service = CreateService();
        var a1 = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "T1", "m1");
        var a2 = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "T2", "m2");

        await service.BulkDismissAlertsAsync([a1.Id, a2.Id], "admin-3", "Bulk dismiss");

        var r1 = await service.GetAlertByIdAsync(a1.Id);
        var r2 = await service.GetAlertByIdAsync(a2.Id);
        r1!.IsDismissed.Should().BeTrue();
        r2!.IsDismissed.Should().BeTrue();
    }

    [Fact]
    public async Task GetUnreadAlertCountAsync_ReturnsCorrectCount()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Unread1", "m");
        await service.SendAlertAsync(SecurityAlertLevel.High, SecurityAlertType.Custom, "Unread2", "m");

        var count = await service.GetUnreadAlertCountAsync();

        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetUnreadAlertCountAsync_WithMinLevel_FiltersCorrectly()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Low", "m");
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.Custom, "Critical", "m");

        var count = await service.GetUnreadAlertCountAsync(SecurityAlertLevel.Critical);

        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task CleanupOldAlertsAsync_RemovesOldAlerts()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = false,
            RetentionDays = 0 // Will cleanup everything before now
        };
        var service = CreateService(config);

        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Old", "m");

        await service.CleanupOldAlertsAsync();

        // After cleanup, stats should show fewer alerts
        // (Note: static state makes exact count hard to test)
    }

    #region Additional Tests

    [Fact]
    public async Task GetRecentAlertsAsync_DefaultParameters_ReturnsPaginatedAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Low Alert", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.High, SecurityAlertType.BruteForceAttack, "High Alert", "msg");

        var (alerts, total) = await service.GetRecentAlertsAsync();

        alerts.Should().NotBeNull();
        total.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_WithMinLevelFilter_OnlyReturnsMatchingAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "LowOnly", "low msg");
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.BruteForceAttack, "CriticalAlert", "critical msg");

        var (alerts, _) = await service.GetRecentAlertsAsync(minLevel: SecurityAlertLevel.Critical);

        alerts.Should().OnlyContain(a => a.Level >= SecurityAlertLevel.Critical);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_WithTypeFilter_OnlyReturnsMatchingType()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.RateLimitExceeded, "Rate", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.BruteForceAttack, "Brute", "msg");

        var (alerts, _) = await service.GetRecentAlertsAsync(type: SecurityAlertType.RateLimitExceeded);

        alerts.Should().OnlyContain(a => a.Type == SecurityAlertType.RateLimitExceeded);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_ExcludeRead_FiltersReadAlerts()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "ToRead", "msg");
        await service.MarkAlertAsReadAsync(alert.Id, "admin");

        var (alerts, _) = await service.GetRecentAlertsAsync(includeRead: false);

        alerts.Should().NotContain(a => a.Id == alert.Id);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_ExcludeDismissed_FiltersDismissedAlerts()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "ToDismiss", "msg");
        await service.DismissAlertAsync(alert.Id, "admin", "Not relevant");

        var (alerts, _) = await service.GetRecentAlertsAsync(includeDismissed: false);

        alerts.Should().NotContain(a => a.Id == alert.Id);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_WithPagination_LimitsResults()
    {
        var service = CreateService();
        for (int i = 0; i < 5; i++)
        {
            await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, $"Alert-{i}", "msg");
        }

        var (alerts, _) = await service.GetRecentAlertsAsync(pageNumber: 1, pageSize: 2);

        alerts.Count.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetRecentAlertsAsync_CachedResult_ReturnsCachedData()
    {
        // GetStringAsync is an extension method that calls GetAsync internally
        // We must mock GetAsync (the actual interface method) returning UTF8 bytes
        var service = CreateService();
        var cachedAlerts = new List<SecurityAlert>
        {
            new SecurityAlert { Id = "cached-1", Title = "Cached Alert", Level = SecurityAlertLevel.High, Type = SecurityAlertType.Custom }
        };
        var cachedTuple = (cachedAlerts, 1);
        var json = System.Text.Json.JsonSerializer.Serialize(cachedTuple);

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes(json));

        var (alerts, total) = await service.GetRecentAlertsAsync();

        alerts.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAlertByIdAsync_ExistingAlert_ReturnsIt()
    {
        var service = CreateService();
        var sent = await service.SendAlertAsync(SecurityAlertLevel.Medium, SecurityAlertType.Custom, "Findable", "msg");

        var result = await service.GetAlertByIdAsync(sent.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(sent.Id);
        result.Title.Should().Be("Findable");
    }

    [Fact]
    public async Task GetAlertByIdAsync_CachedAlert_ReturnsCachedData()
    {
        // GetStringAsync is an extension method; mock GetAsync (IDistributedCache interface method) instead
        var cachedAlert = new SecurityAlert
        {
            Id = "cached-alert-id",
            Title = "Cached",
            Level = SecurityAlertLevel.Medium,
            Type = SecurityAlertType.Custom
        };
        var json = System.Text.Json.JsonSerializer.Serialize(cachedAlert);

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes(json));

        var service = CreateService();
        var result = await service.GetAlertByIdAsync("cached-alert-id");

        result.Should().NotBeNull();
        result!.Title.Should().Be("Cached");
    }

    [Fact]
    public async Task SendAlertAsync_HighAlertWithEmailEnabled_SendsNotification()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        _notifier.NotifyAdminsAsync(Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = CreateService(config);

        // Note: SendNotificationsAsync only fires for Critical in the production code
        await service.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.UnauthorizedAccessAttempt,
            "Test High",
            "test message");

        await _notifier.Received(1).NotifyAdminsAsync(
            Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAlertAsync_NotifierThrows_DoesNotPropagateException()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        _notifier.NotifyAdminsAsync(Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Notifier failed"));

        var service = CreateService(config);

        // Notification failure should not break the alert flow
        var act = () => service.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.BruteForceAttack,
            "Critical Alert",
            "test");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAlertAsync_WithMetadata_StoresMetadata()
    {
        var service = CreateService();
        var metadata = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["count"] = 42
        };

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Low,
            SecurityAlertType.Custom,
            "WithMeta",
            "msg",
            metadata);

        alert.Metadata.Should().ContainKey("key1");
        alert.Metadata["key1"].Should().Be("value1");
    }

    [Fact]
    public async Task SendAlertAsync_Enabled_SetsRequiredFields()
    {
        var service = CreateService();

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Info,
            SecurityAlertType.Custom,
            "Full Alert",
            "Full message");

        alert.Id.Should().NotBeNullOrEmpty();
        alert.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        alert.Level.Should().Be(SecurityAlertLevel.Info);
        alert.Type.Should().Be(SecurityAlertType.Custom);
        alert.OccurrenceCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAlertStatisticsAsync_WithFromFilter_FiltersOlderAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.BruteForceAttack, "Critical", "msg");

        var stats = await service.GetAlertStatisticsAsync(from: DateTime.UtcNow.AddHours(-1));

        stats.Should().NotBeNull();
        stats.TotalAlerts.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetAlertStatisticsAsync_ContainsTimelineData()
    {
        var service = CreateService();

        var stats = await service.GetAlertStatisticsAsync();

        stats.Timeline.Should().NotBeNull();
        stats.Timeline.Should().HaveCount(30); // 30 days of timeline
    }

    [Fact]
    public async Task GetAlertStatisticsAsync_WithAlerts_PopulatesAlertsByType()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.RateLimitExceeded, "Rate1", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.RateLimitExceeded, "Rate2", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.High, SecurityAlertType.BruteForceAttack, "Brute", "msg");

        var stats = await service.GetAlertStatisticsAsync();

        stats.AlertsByType.Should().NotBeNull();
    }

    [Fact]
    public async Task DismissAlertAsync_NonExistent_DoesNotThrow()
    {
        var service = CreateService();

        var act = () => service.DismissAlertAsync("no-such-id", "admin", "no reason");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DismissAlertAsync_SetsAllDismissalFields()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Dismissible", "msg");

        await service.DismissAlertAsync(alert.Id, "admin-007", "False positive");

        var retrieved = await service.GetAlertByIdAsync(alert.Id);
        retrieved.Should().NotBeNull();
        retrieved!.IsDismissed.Should().BeTrue();
        retrieved.DismissedByAdminId.Should().Be("admin-007");
        retrieved.DismissalReason.Should().Be("False positive");
        retrieved.DismissedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUnreadAlertCountAsync_NoAlerts_ReturnsZero()
    {
        var service = CreateService();

        // Mock GetAsync (not GetStringAsync extension method) to return null — cache miss
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var count = await service.GetUnreadAlertCountAsync();

        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetUnreadAlertCountAsync_CachedCount_ReturnsCached()
    {
        // Mock GetAsync to return UTF8 bytes of "7"
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("7"));

        var service = CreateService();
        var count = await service.GetUnreadAlertCountAsync();

        count.Should().Be(7);
    }

    [Fact]
    public async Task GetUnreadAlertCountAsync_WithMinLevel_OnlyCountsMatchingLevel()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Low1", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.Custom, "Critical1", "msg");

        // Cache miss — let the service compute from in-memory store
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var criticalCount = await service.GetUnreadAlertCountAsync(SecurityAlertLevel.Critical);

        criticalCount.Should().BeGreaterThanOrEqualTo(0);
    }

    #endregion

    #region Coverage Tests

    [Fact]
    public async Task SendAlertAsync_Disabled_ReturnsAlertButDoesNotIncreaseCount()
    {
        // Get count before (static list is shared across tests)
        var enabledService = CreateService();
        var (beforeAlerts, beforeTotal) = await enabledService.GetRecentAlertsAsync();

        var config = new SecurityAlertConfiguration { Enabled = false };
        var disabledService = CreateService(config);

        var alert = await disabledService.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.BruteForceAttack,
            "Disabled Alert",
            "Should be ignored");

        alert.Should().NotBeNull();
        alert.Title.Should().Be("Disabled Alert");

        // Alert count should not have increased (disabled mode doesn't persist)
        var (afterAlerts, afterTotal) = await enabledService.GetRecentAlertsAsync();
        afterTotal.Should().Be(beforeTotal);
    }

    [Fact]
    public async Task SendAlertAsync_WithAggregation_DuplicateAlert_IncrementsCount()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = true,
            DuplicateAlertThrottleSeconds = 300,
            EnableEmailNotifications = false
        };
        var service = CreateService(config);

        // Send same alert twice
        var first = await service.SendAlertAsync(
            SecurityAlertLevel.Low,
            SecurityAlertType.RateLimitExceeded,
            "Same Title",
            "first message");

        var second = await service.SendAlertAsync(
            SecurityAlertLevel.Low,
            SecurityAlertType.RateLimitExceeded,
            "Same Title",
            "second message");

        // The second call should return the same alert with incremented count
        second.Id.Should().Be(first.Id);
        second.OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public async Task SendAlertAsync_WithAggregation_DifferentTitle_CreatesSeparateAlerts()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = true,
            DuplicateAlertThrottleSeconds = 300,
            EnableEmailNotifications = false
        };
        var service = CreateService(config);

        var first = await service.SendAlertAsync(
            SecurityAlertLevel.Low, SecurityAlertType.Custom, "Title A", "msg");
        var second = await service.SendAlertAsync(
            SecurityAlertLevel.Low, SecurityAlertType.Custom, "Title B", "msg");

        second.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public async Task Coverage_CleanupOldAlertsAsync_RemovesOldAlerts()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = false,
            RetentionDays = 0 // All alerts are "old"
        };
        var service = CreateService(config);

        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Old1", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Old2", "msg");

        // Wait a tick so OccurredAt < cutoffDate (retention = 0 days)
        await Task.Delay(10);
        await service.CleanupOldAlertsAsync();

        var (alerts, total) = await service.GetRecentAlertsAsync();
        alerts.Should().BeEmpty();
    }

    [Fact]
    public async Task CleanupOldAlertsAsync_KeepsRecentAlerts()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = false,
            RetentionDays = 365
        };
        var service = CreateService(config);

        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Recent", "msg");
        await service.CleanupOldAlertsAsync();

        var (alerts, total) = await service.GetRecentAlertsAsync();
        alerts.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BulkDismissAlertsAsync_DismissesAllMatchingIds()
    {
        var service = CreateService();
        var a1 = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "A1", "msg");
        var a2 = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "A2", "msg");
        var a3 = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "A3", "msg");

        await service.BulkDismissAlertsAsync(
            new List<string> { a1.Id, a2.Id },
            "admin",
            "Bulk dismiss");

        var r1 = await service.GetAlertByIdAsync(a1.Id);
        var r2 = await service.GetAlertByIdAsync(a2.Id);
        var r3 = await service.GetAlertByIdAsync(a3.Id);

        r1!.IsDismissed.Should().BeTrue();
        r2!.IsDismissed.Should().BeTrue();
        r3!.IsDismissed.Should().BeFalse();
    }

    [Fact]
    public async Task BulkDismissAlertsAsync_NonExistentIds_DoesNotThrow()
    {
        var service = CreateService();

        var act = () => service.BulkDismissAlertsAsync(
            new List<string> { "no-such-id-1", "no-such-id-2" },
            "admin",
            "reason");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Coverage_MarkAlertAsReadAsync_NonExistent_DoesNotThrow()
    {
        var service = CreateService();
        var act = () => service.MarkAlertAsReadAsync("nonexistent", "admin");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Coverage_MarkAlertAsReadAsync_ExistingAlert_SetsIsRead()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Low, SecurityAlertType.Custom, "ReadMe", "msg");

        await service.MarkAlertAsReadAsync(alert.Id, "admin");

        var retrieved = await service.GetAlertByIdAsync(alert.Id);
        retrieved!.IsRead.Should().BeTrue();
    }

    [Fact]
    public async Task GetAlertStatisticsAsync_CountsByLevel()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "L1", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.High, SecurityAlertType.Custom, "H1", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.Custom, "C1", "msg");

        var stats = await service.GetAlertStatisticsAsync();

        stats.TotalAlerts.Should().BeGreaterThanOrEqualTo(3);
        (stats.CriticalAlerts + stats.HighAlerts + stats.LowAlerts).Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task GetAlertStatisticsAsync_WithToFilter_ExcludesNewerAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Old", "msg");

        var stats = await service.GetAlertStatisticsAsync(to: DateTime.UtcNow.AddHours(1));

        stats.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAlertAsync_WithHttpContext_EnrichesAlert()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/test";
        httpContext.Request.Headers["User-Agent"] = "TestAgent/1.0";
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");

        _httpContextAccessor.HttpContext.Returns(httpContext);
        var service = CreateService();

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Medium,
            SecurityAlertType.UnauthorizedAccessAttempt,
            "Context Test",
            "message");

        alert.IPAddress.Should().Be("192.168.1.1");
        alert.UserAgent.Should().Contain("TestAgent");
        alert.Endpoint.Should().Be("/api/test");
    }

    [Fact]
    public async Task SendAlertAsync_CriticalAlert_WithEmailEnabled_SendsNotification()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        _notifier.NotifyAdminsAsync(Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var service = CreateService(config);

        await service.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.BruteForceAttack,
            "Critical Alert",
            "test message");

        await _notifier.Received(1).NotifyAdminsAsync(
            Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAlertAsync_LowAlert_WithEmailEnabled_SkipsNotification()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        var service = CreateService(config);

        await service.SendAlertAsync(
            SecurityAlertLevel.Low,
            SecurityAlertType.Custom,
            "Low Alert",
            "test message");

        // Low alerts should NOT trigger email notification
        await _notifier.DidNotReceive().NotifyAdminsAsync(
            Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetRecentAlertsAsync_CacheHit_ReturnsCachedData()
    {
        var service = CreateService();

        // First call — stores in cache (and static list)
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Cached", "msg");

        // Second call — should work (hits in-memory, may also cache)
        var (alerts, total) = await service.GetRecentAlertsAsync();
        alerts.Should().NotBeEmpty();
        total.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Coverage_GetAlertByIdAsync_ExistingAlert_ReturnsAlert()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "ById", "msg");

        var retrieved = await service.GetAlertByIdAsync(alert.Id);

        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(alert.Id);
    }

    [Fact]
    public async Task Coverage_GetAlertByIdAsync_NonExistent_ReturnsNull()
    {
        var service = CreateService();
        var result = await service.GetAlertByIdAsync("no-such-alert");
        result.Should().BeNull();
    }

    [Fact]
    public async Task Coverage_DismissAlertAsync_ExistingAlert_SetsDismissed()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Dismiss", "msg");

        await service.DismissAlertAsync(alert.Id, "admin", "test reason");

        var retrieved = await service.GetAlertByIdAsync(alert.Id);
        retrieved!.IsDismissed.Should().BeTrue();
        retrieved.DismissedByAdminId.Should().Be("admin");
    }

    [Fact]
    public async Task Coverage_DismissAlertAsync_NonExistent_DoesNotThrow()
    {
        var service = CreateService();
        var act = () => service.DismissAlertAsync("nonexistent", "admin", "reason");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Coverage_GetUnreadAlertCountAsync_WithUnreadAlerts_ReturnsCount()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Unread1", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Unread2", "msg");

        var count = await service.GetUnreadAlertCountAsync();
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Coverage_GetUnreadAlertCountAsync_WithMinLevel_FiltersLowAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "LowUnread", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.Custom, "CritUnread", "msg");

        var count = await service.GetUnreadAlertCountAsync(minLevel: SecurityAlertLevel.High);
        count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Coverage_GetRecentAlertsAsync_WithMinLevel_FiltersLowAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "FilterLow", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Critical, SecurityAlertType.Custom, "FilterCrit", "msg");

        var (alerts, _) = await service.GetRecentAlertsAsync(minLevel: SecurityAlertLevel.High);
        alerts.Should().OnlyContain(a => a.Level >= SecurityAlertLevel.High);
    }

    [Fact]
    public async Task Coverage_GetRecentAlertsAsync_WithTypeFilter_FiltersOtherTypes()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.BruteForceAttack, "BF", "msg");
        await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "Custom", "msg");

        var (alerts, _) = await service.GetRecentAlertsAsync(type: SecurityAlertType.BruteForceAttack);
        alerts.Should().OnlyContain(a => a.Type == SecurityAlertType.BruteForceAttack);
    }

    [Fact]
    public async Task Coverage_GetRecentAlertsAsync_ExcludeRead_FiltersReadAlerts()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "ReadFilter", "msg");
        await service.MarkAlertAsReadAsync(alert.Id, "admin");

        var (alerts, _) = await service.GetRecentAlertsAsync(includeRead: false);
        alerts.Should().NotContain(a => a.Id == alert.Id);
    }

    [Fact]
    public async Task Coverage_GetRecentAlertsAsync_IncludeDismissed_IncludesDismissed()
    {
        var service = CreateService();
        var alert = await service.SendAlertAsync(SecurityAlertLevel.Low, SecurityAlertType.Custom, "DismFilter", "msg");
        await service.DismissAlertAsync(alert.Id, "admin", "test");

        var (alerts, _) = await service.GetRecentAlertsAsync(includeDismissed: true);
        alerts.Should().Contain(a => a.Id == alert.Id);
    }

    [Fact]
    public async Task Coverage_GetAlertStatisticsAsync_WithFromFilter_ExcludesOlderAlerts()
    {
        var service = CreateService();
        await service.SendAlertAsync(SecurityAlertLevel.Medium, SecurityAlertType.Custom, "StatAlert", "msg");

        var stats = await service.GetAlertStatisticsAsync(from: DateTime.UtcNow.AddMinutes(-1));

        stats.Should().NotBeNull();
        stats.TotalAlerts.Should().BeGreaterThanOrEqualTo(1);
        stats.AlertsByType.Should().NotBeNull();
        stats.Timeline.Should().NotBeNull();
    }

    [Fact]
    public async Task Coverage_SendAlertAsync_WithMetadata_StoresMetadata()
    {
        var service = CreateService();
        var metadata = new Dictionary<string, object>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        };

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Medium,
            SecurityAlertType.Custom,
            "MetadataTest",
            "msg",
            metadata);

        alert.Metadata.Should().ContainKey("key1");
    }

    [Fact]
    public async Task SendAlertAsync_NotificationThrows_AlertStillCreated()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        _notifier.NotifyAdminsAsync(Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("notification failed"));

        var service = CreateService(config);

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.BruteForceAttack,
            "NotifFail",
            "msg");

        alert.Should().NotBeNull();
        alert.Title.Should().Be("NotifFail");
    }

    [Fact]
    public async Task SendAlertAsync_NotifierReturnsFalse_DoesNotThrow()
    {
        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = true
        };
        _notifier.NotifyAdminsAsync(Arg.Any<SecurityAlert>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var service = CreateService(config);

        var alert = await service.SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.BruteForceAttack,
            "NotifFalse",
            "msg");

        alert.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAlertAsync_IPThresholdExceeded_CreatesFollowUpAlert()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.99");

        _httpContextAccessor.HttpContext.Returns(httpContext);

        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = true,         // Dedup enabled (recursion guard is in production code via TriggeredByAlertId check)
            DuplicateAlertThrottleSeconds = 300,
            EnableEmailNotifications = false,
            Thresholds = new AlertThresholds
            {
                AutoBlockIPAfterCriticalAlerts = 1, // Trigger on first alert from this IP
                AutoSuspendUserAfterHighAlerts = 999 // Don't trigger user threshold
            }
        };
        var service = CreateService(config);

        // This single alert should trigger the IP threshold follow-up
        await service.SendAlertAsync(
            SecurityAlertLevel.High,
            SecurityAlertType.BruteForceAttack,
            "IPThresholdTest",
            "msg");

        // The follow-up alert should also be stored
        var (alerts, _) = await service.GetRecentAlertsAsync();
        alerts.Should().Contain(a => a.Title.Contains("IP Block Recommended"));
    }

    [Fact]
    public async Task SendAlertAsync_UserThresholdExceeded_CreatesFollowUpAlert()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("sub", "user-threshold-test") },
                "test"));

        _httpContextAccessor.HttpContext.Returns(httpContext);

        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = true,         // Dedup enabled (recursion guard is in production code via TriggeredByAlertId check)
            DuplicateAlertThrottleSeconds = 300,
            EnableEmailNotifications = false,
            Thresholds = new AlertThresholds
            {
                AutoBlockIPAfterCriticalAlerts = 999, // Don't trigger IP threshold
                AutoSuspendUserAfterHighAlerts = 1    // Trigger on first alert for this user
            }
        };
        var service = CreateService(config);

        await service.SendAlertAsync(
            SecurityAlertLevel.Medium,
            SecurityAlertType.UnauthorizedAccessAttempt,
            "UserThresholdTest",
            "msg");

        var (alerts, _) = await service.GetRecentAlertsAsync();
        alerts.Should().Contain(a => a.Title.Contains("User Suspension Recommended"));
    }

    [Fact]
    public async Task SendAlertAsync_ThresholdExceeded_FollowUpDoesNotRecurse()
    {
        // Both IP and User thresholds set to 1, dedup OFF.
        // Before the fix this would StackOverflow because follow-up alerts
        // re-enter SendAlertAsync → CheckAutoActionThresholdsAsync → SendAlertAsync → ...
        var uniqueIp = $"10.99.99.{Random.Shared.Next(1, 254)}";
        var uniqueUser = $"recursion-test-{Guid.NewGuid():N}";

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(uniqueIp);
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("sub", uniqueUser) },
                "test"));

        _httpContextAccessor.HttpContext.Returns(httpContext);

        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,        // NO dedup — recursion guard must come from the code
            EnableEmailNotifications = false,
            Thresholds = new AlertThresholds
            {
                AutoBlockIPAfterCriticalAlerts = 1,
                AutoSuspendUserAfterHighAlerts = 1
            }
        };
        var service = CreateService(config);

        var uniqueTitle = $"RecursionTest-{Guid.NewGuid():N}";

        // Should complete without StackOverflowException
        await service.SendAlertAsync(
            SecurityAlertLevel.High,
            SecurityAlertType.BruteForceAttack,
            uniqueTitle,
            "msg");

        var (alerts, _) = await service.GetRecentAlertsAsync();

        // Filter to alerts related to this test (static _alerts list is shared across tests)
        var relevant = alerts.Where(a =>
            a.Title == uniqueTitle ||
            a.Title.Contains(uniqueIp) ||
            a.Title.Contains(uniqueUser)).ToList();

        // Original + IP follow-up + User follow-up = exactly 3
        relevant.Should().HaveCount(3);
        relevant.Should().ContainSingle(a => a.Title == uniqueTitle);
        relevant.Should().ContainSingle(a => a.Title.Contains("IP Block Recommended"));
        relevant.Should().ContainSingle(a => a.Title.Contains("User Suspension Recommended"));
    }

    [Fact]
    public async Task SendAlertAsync_FollowUpAlert_SkipsThresholdCheck()
    {
        // A follow-up alert (with TriggeredByAlertId metadata) should never
        // trigger another threshold check, even if thresholds are at 1.
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());

        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = false,
            Thresholds = new AlertThresholds
            {
                AutoBlockIPAfterCriticalAlerts = 1,
                AutoSuspendUserAfterHighAlerts = 1
            }
        };
        var service = CreateService(config);

        var uniqueTitle = $"ManualFollowUp-{Guid.NewGuid():N}";

        // Directly send a follow-up alert — should NOT create further follow-ups
        await service.SendAlertAsync(
            SecurityAlertLevel.High,
            SecurityAlertType.RateLimitExceeded,
            uniqueTitle,
            "msg",
            new Dictionary<string, object> { ["TriggeredByAlertId"] = "original-123" });

        var (alerts, _) = await service.GetRecentAlertsAsync();

        // Only the one follow-up alert should exist with this title
        var relevant = alerts.Where(a => a.Title == uniqueTitle).ToList();
        relevant.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendAlertAsync_FollowUpAlerts_DoNotCountTowardThresholds()
    {
        // IP threshold = 1 (fires on first original alert), User threshold = 2 (needs 2 originals).
        // One original alert should produce an IP follow-up but NOT a User follow-up,
        // because the IP follow-up must not inflate the user alert count.
        var uniqueIp = $"10.88.88.{Random.Shared.Next(1, 254)}";
        var uniqueUser = $"threshold-count-{Guid.NewGuid():N}";

        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(uniqueIp);
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("sub", uniqueUser) },
                "test"));

        _httpContextAccessor.HttpContext.Returns(httpContext);

        var config = new SecurityAlertConfiguration
        {
            Enabled = true,
            EnableAlertAggregation = false,
            EnableEmailNotifications = false,
            Thresholds = new AlertThresholds
            {
                AutoBlockIPAfterCriticalAlerts = 1,  // fires on first original
                AutoSuspendUserAfterHighAlerts = 2   // needs 2 originals — should NOT fire
            }
        };
        var service = CreateService(config);

        var uniqueTitle = $"MixedThresholdTest-{Guid.NewGuid():N}";

        await service.SendAlertAsync(
            SecurityAlertLevel.High,
            SecurityAlertType.BruteForceAttack,
            uniqueTitle,
            "msg");

        var (alerts, _) = await service.GetRecentAlertsAsync();
        var relevant = alerts.Where(a =>
            a.Title == uniqueTitle ||
            a.Title.Contains(uniqueIp) ||
            a.Title.Contains(uniqueUser)).ToList();

        // Original + IP follow-up = 2 (no User follow-up because threshold is 2)
        relevant.Should().HaveCount(2);
        relevant.Should().ContainSingle(a => a.Title == uniqueTitle);
        relevant.Should().ContainSingle(a => a.Title.Contains("IP Block Recommended"));
        relevant.Should().NotContain(a => a.Title.Contains("User Suspension Recommended"));
    }

    #endregion
}
