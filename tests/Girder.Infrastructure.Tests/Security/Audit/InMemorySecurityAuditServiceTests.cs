using Infrastructure.Security.Audit;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security.Audit;

[Trait("Category", "Unit")]
public class InMemorySecurityAuditServiceTests
{
    private readonly InMemorySecurityAuditService _sut;
    private readonly ILogger<InMemorySecurityAuditService> _logger = Substitute.For<ILogger<InMemorySecurityAuditService>>();

    public InMemorySecurityAuditServiceTests()
    {
        _sut = new InMemorySecurityAuditService(_logger);
    }

    #region LogSecurityEventAsync (event overload)

    [Fact]
    public async Task LogSecurityEventAsync_Event_ReturnsEventId()
    {
        var evt = CreateEvent();

        var result = await _sut.LogSecurityEventAsync(evt);

        result.Should().Be(evt.Id);
    }

    [Fact]
    public async Task LogSecurityEventAsync_Event_StoresEvent()
    {
        var evt = CreateEvent("MyLoginEvent");

        await _sut.LogSecurityEventAsync(evt);

        var events = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { PageSize = 100 });
        events.Should().Contain(e => e.Id == evt.Id);
    }

    [Fact]
    public async Task LogSecurityEventAsync_MultipleEvents_AllStored()
    {
        for (var i = 0; i < 5; i++)
        {
            await _sut.LogSecurityEventAsync(CreateEvent($"Event{i}"));
        }

        var events = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { PageSize = 100 });
        events.Count().Should().Be(5);
    }

    #endregion

    #region LogSecurityEventAsync (string overload)

    [Fact]
    public async Task LogSecurityEventAsync_StringOverload_StoresEvent()
    {
        var result = await _sut.LogSecurityEventAsync(
            "LoginAttempt",
            "User login attempt",
            SecurityEventSeverity.Information);

        result.Should().NotBeNullOrEmpty();

        var events = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { EventType = "Login" });
        events.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LogSecurityEventAsync_StringOverload_WithAdditionalData_StoresEvent()
    {
        var result = await _sut.LogSecurityEventAsync(
            "PasswordChange",
            "User changed password",
            SecurityEventSeverity.Medium,
            new { UserId = "user-1", Reason = "Expiry" });

        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region GetSecurityEventsAsync

    [Fact]
    public async Task GetSecurityEventsAsync_EmptyStore_ReturnsEmpty()
    {
        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSecurityEventsAsync_FilterByUserId_ReturnsMatchingEvents()
    {
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "E1", UserId = "alice", Description = "A" });
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "E2", UserId = "bob", Description = "B" });

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { UserId = "alice" });

        result.Should().OnlyContain(e => e.UserId == "alice");
    }

    [Fact]
    public async Task GetSecurityEventsAsync_FilterByEventType_ReturnsMatchingEvents()
    {
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "Login", Description = "login" });
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "Logout", Description = "logout" });

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { EventType = "Login" });

        result.Should().OnlyContain(e => e.EventType.Contains("Login"));
    }

    [Fact]
    public async Task GetSecurityEventsAsync_FilterBySeverity_ReturnsMatchingEvents()
    {
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "A", Severity = SecurityEventSeverity.High, Description = "H" });
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "B", Severity = SecurityEventSeverity.Information, Description = "I" });

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { Severity = SecurityEventSeverity.High });

        result.Should().OnlyContain(e => e.Severity == SecurityEventSeverity.High);
    }

    [Fact]
    public async Task GetSecurityEventsAsync_FilterByFromDate_ReturnsEventsAfterDate()
    {
        var old = new SecurityAuditEvent { EventType = "Old", Description = "O", Timestamp = DateTime.UtcNow.AddDays(-10) };
        var recent = new SecurityAuditEvent { EventType = "Recent", Description = "R", Timestamp = DateTime.UtcNow };
        await _sut.LogSecurityEventAsync(old);
        await _sut.LogSecurityEventAsync(recent);

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery
        {
            FromDate = DateTime.UtcNow.AddDays(-1)
        });

        result.Should().OnlyContain(e => e.EventType == "Recent");
    }

    [Fact]
    public async Task GetSecurityEventsAsync_FilterByToDate_ReturnsEventsBeforeDate()
    {
        var old = new SecurityAuditEvent { EventType = "Old", Description = "O", Timestamp = DateTime.UtcNow.AddDays(-10) };
        var recent = new SecurityAuditEvent { EventType = "Recent", Description = "R", Timestamp = DateTime.UtcNow };
        await _sut.LogSecurityEventAsync(old);
        await _sut.LogSecurityEventAsync(recent);

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery
        {
            ToDate = DateTime.UtcNow.AddDays(-5)
        });

        result.Should().OnlyContain(e => e.EventType == "Old");
    }

    [Fact]
    public async Task GetSecurityEventsAsync_Pagination_RespectsPageSize()
    {
        for (var i = 0; i < 10; i++)
        {
            await _sut.LogSecurityEventAsync(CreateEvent($"Evt{i}"));
        }

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { Page = 1, PageSize = 3 });

        result.Count().Should().Be(3);
    }

    [Fact]
    public async Task GetSecurityEventsAsync_Pagination_SecondPage_ReturnsNextBatch()
    {
        for (var i = 0; i < 10; i++)
        {
            await _sut.LogSecurityEventAsync(CreateEvent($"Evt{i}"));
        }

        var page1 = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { Page = 1, PageSize = 5 });
        var page2 = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { Page = 2, PageSize = 5 });

        page1.Select(e => e.Id).Should().NotIntersectWith(page2.Select(e => e.Id));
    }

    #endregion

    #region VerifyAuditIntegrityAsync

    [Fact]
    public async Task VerifyAuditIntegrityAsync_Always_ReturnsIntact()
    {
        await _sut.LogSecurityEventAsync(CreateEvent("TestEvent"));

        var result = await _sut.VerifyAuditIntegrityAsync();

        result.IsIntegrityIntact.Should().BeTrue();
        result.IntegrityViolations.Should().Be(0);
    }

    [Fact]
    public async Task VerifyAuditIntegrityAsync_Empty_ReturnsIntact()
    {
        var result = await _sut.VerifyAuditIntegrityAsync();

        result.IsIntegrityIntact.Should().BeTrue();
        result.EventsVerified.Should().Be(0);
    }

    #endregion

    #region GenerateAuditReportAsync

    [Fact]
    public async Task GenerateAuditReportAsync_Empty_ReturnsEmptyReport()
    {
        var query = new SecurityAuditQuery();

        var report = await _sut.GenerateAuditReportAsync(query);

        report.TotalEvents.Should().Be(0);
        report.EventsBySeverity.Should().BeEmpty();
        report.EventsByCategory.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAuditReportAsync_WithEvents_ReturnsCorrectReport()
    {
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent
        {
            EventType = "Login", Description = "L", Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.Authentication
        });
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent
        {
            EventType = "FailedLogin", Description = "FL", Severity = SecurityEventSeverity.High,
            Category = SecurityEventCategory.Authentication
        });

        var report = await _sut.GenerateAuditReportAsync(new SecurityAuditQuery { PageSize = 100 });

        report.TotalEvents.Should().Be(2);
        report.EventsBySeverity.Should().ContainKey(SecurityEventSeverity.Information);
        report.EventsBySeverity.Should().ContainKey(SecurityEventSeverity.High);
        report.EventsByCategory.Should().ContainKey(SecurityEventCategory.Authentication);
    }

    [Fact]
    public async Task GenerateAuditReportAsync_FiltersWithDateRange()
    {
        var old = new SecurityAuditEvent
        {
            EventType = "Old", Description = "O", Timestamp = DateTime.UtcNow.AddDays(-30),
            Severity = SecurityEventSeverity.Low, Category = SecurityEventCategory.General
        };
        var recent = new SecurityAuditEvent
        {
            EventType = "Recent", Description = "R", Timestamp = DateTime.UtcNow,
            Severity = SecurityEventSeverity.Low, Category = SecurityEventCategory.General
        };
        await _sut.LogSecurityEventAsync(old);
        await _sut.LogSecurityEventAsync(recent);

        var query = new SecurityAuditQuery
        {
            FromDate = DateTime.UtcNow.AddDays(-1),
            ToDate = DateTime.UtcNow.AddDays(1),
            PageSize = 100
        };
        var report = await _sut.GenerateAuditReportAsync(query);

        report.TotalEvents.Should().Be(1);
    }

    #endregion

    #region ExportAuditLogsAsync

    [Fact]
    public async Task ExportAuditLogsAsync_ReturnsJsonBytes()
    {
        await _sut.LogSecurityEventAsync(CreateEvent("ExportTest"));

        var bytes = await _sut.ExportAuditLogsAsync(new SecurityAuditQuery { PageSize = 100 });

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(0);
    }

    #endregion

    #region ArchiveOldLogsAsync

    [Fact]
    public async Task ArchiveOldLogsAsync_RemovesOldEvents_ReturnsCount()
    {
        var old = new SecurityAuditEvent { EventType = "Old", Description = "O", Timestamp = DateTime.UtcNow.AddDays(-10) };
        var recent = new SecurityAuditEvent { EventType = "Recent", Description = "R", Timestamp = DateTime.UtcNow };
        await _sut.LogSecurityEventAsync(old);
        await _sut.LogSecurityEventAsync(recent);

        var count = await _sut.ArchiveOldLogsAsync(DateTime.UtcNow.AddDays(-5));

        count.Should().Be(1);

        var remaining = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery { PageSize = 100 });
        remaining.Should().OnlyContain(e => e.EventType == "Recent");
    }

    [Fact]
    public async Task ArchiveOldLogsAsync_NoOldEvents_ReturnsZero()
    {
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "Fresh", Description = "F", Timestamp = DateTime.UtcNow });

        var count = await _sut.ArchiveOldLogsAsync(DateTime.UtcNow.AddDays(-30));

        count.Should().Be(0);
    }

    #endregion

    #region GetAuditStatisticsAsync

    [Fact]
    public async Task GetAuditStatisticsAsync_Empty_ReturnsTotalZero()
    {
        var result = await _sut.GetAuditStatisticsAsync();

        result.TotalEvents.Should().Be(0);
        result.OldestEventTimestamp.Should().BeNull();
        result.NewestEventTimestamp.Should().BeNull();
    }

    [Fact]
    public async Task GetAuditStatisticsAsync_WithEvents_ReturnsCorrectStats()
    {
        var ts1 = DateTime.UtcNow.AddDays(-5);
        var ts2 = DateTime.UtcNow;
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "A", Description = "a", Timestamp = ts1 });
        await _sut.LogSecurityEventAsync(new SecurityAuditEvent { EventType = "B", Description = "b", Timestamp = ts2 });

        var result = await _sut.GetAuditStatisticsAsync();

        result.TotalEvents.Should().Be(2);
        result.OldestEventTimestamp.Should().BeCloseTo(ts1, TimeSpan.FromSeconds(1));
        result.NewestEventTimestamp.Should().BeCloseTo(ts2, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task GetAuditStatisticsAsync_WithDateRange_FiltersEvents()
    {
        var old = new SecurityAuditEvent { EventType = "Old", Description = "O", Timestamp = DateTime.UtcNow.AddDays(-20) };
        var recent = new SecurityAuditEvent { EventType = "Recent", Description = "R", Timestamp = DateTime.UtcNow };
        await _sut.LogSecurityEventAsync(old);
        await _sut.LogSecurityEventAsync(recent);

        var result = await _sut.GetAuditStatisticsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        result.TotalEvents.Should().Be(1);
    }

    #endregion

    private static SecurityAuditEvent CreateEvent(string type = "TestEvent") =>
        new SecurityAuditEvent
        {
            EventType = type,
            Description = $"Test: {type}",
            Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.General
        };
}
