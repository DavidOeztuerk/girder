using Girder.Abstractions.Security.Audit;
using Girder.Infrastructure.Security.Audit;

namespace Girder.Infrastructure.Tests.Security.Audit;

[Trait("Category", "Unit")]
public class SecurityAuditOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new SecurityAuditOptions();

        options.EnableAuditLogging.Should().BeTrue();
        options.DefaultRetentionDays.Should().Be(2555);
        options.ArchiveAfterDays.Should().Be(365);
        options.MaxEvents.Should().Be(1000000);
        options.SigningKey.Should().BeNull();
        options.EnableIntegrityVerification.Should().BeTrue();
        options.IntegrityVerificationIntervalHours.Should().Be(24);
        options.LogAllRequests.Should().BeFalse();
        options.IncludeRequestBodies.Should().BeTrue();
        options.MaxRequestBodySize.Should().Be(1024 * 1024);
        options.ComplianceRequirements.Should().Contain("GDPR");
        options.SupportedExportFormats.Should().Contain("JSON");
        options.SupportedExportFormats.Should().Contain("CSV");
        options.SupportedExportFormats.Should().Contain("XML");
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var options = new SecurityAuditOptions
        {
            EnableAuditLogging = false,
            DefaultRetentionDays = 30,
            ArchiveAfterDays = 90,
            MaxEvents = 500000,
            SigningKey = "base64key==",
            EnableIntegrityVerification = false,
            IntegrityVerificationIntervalHours = 48,
            LogAllRequests = true,
            IncludeRequestBodies = false,
            MaxRequestBodySize = 512,
            ComplianceRequirements = new List<string> { "SOX" },
            SupportedExportFormats = new List<string> { "JSON" }
        };

        options.EnableAuditLogging.Should().BeFalse();
        options.DefaultRetentionDays.Should().Be(30);
        options.ArchiveAfterDays.Should().Be(90);
        options.MaxEvents.Should().Be(500000);
        options.SigningKey.Should().Be("base64key==");
        options.EnableIntegrityVerification.Should().BeFalse();
        options.IntegrityVerificationIntervalHours.Should().Be(48);
        options.LogAllRequests.Should().BeTrue();
        options.IncludeRequestBodies.Should().BeFalse();
        options.MaxRequestBodySize.Should().Be(512);
        options.ComplianceRequirements.Should().Contain("SOX");
        options.SupportedExportFormats.Should().HaveCount(1);
    }
}

[Trait("Category", "Unit")]
public class SecurityAuditStatisticsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var stats = new SecurityAuditStatistics();

        stats.TotalEvents.Should().Be(0);
        stats.AverageEventsPerDay.Should().Be(0);
        stats.StorageSizeBytes.Should().Be(0);
        stats.OldestEventTimestamp.Should().BeNull();
        stats.NewestEventTimestamp.Should().BeNull();
        stats.EventsPerDay.Should().NotBeNull();
        stats.EventsPerDay.Should().BeEmpty();
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var now = DateTime.UtcNow;
        var yesterday = now.AddDays(-1);
        var stats = new SecurityAuditStatistics
        {
            TotalEvents = 100,
            AverageEventsPerDay = 50.5,
            StorageSizeBytes = 1024000,
            OldestEventTimestamp = yesterday,
            NewestEventTimestamp = now,
            EventsPerDay = new Dictionary<DateTime, int> { [yesterday] = 42 }
        };

        stats.TotalEvents.Should().Be(100);
        stats.AverageEventsPerDay.Should().Be(50.5);
        stats.StorageSizeBytes.Should().Be(1024000);
        stats.OldestEventTimestamp.Should().Be(yesterday);
        stats.NewestEventTimestamp.Should().Be(now);
        stats.EventsPerDay.Should().ContainKey(yesterday);
    }
}

[Trait("Category", "Unit")]
public class SecurityAuditQueryTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var query = new SecurityAuditQuery();

        query.FromDate.Should().BeNull();
        query.ToDate.Should().BeNull();
        query.UserId.Should().BeNull();
        query.EventType.Should().BeNull();
        query.Severity.Should().BeNull();
        query.Category.Should().BeNull();
        query.Source.Should().BeNull();
        query.IpAddress.Should().BeNull();
        query.ResourceType.Should().BeNull();
        query.ResourceId.Should().BeNull();
        query.Tags.Should().NotBeNull();
        query.Tags.Should().BeEmpty();
        query.SearchText.Should().BeNull();
        query.Page.Should().Be(1);
        query.PageSize.Should().Be(100);
        query.SortBy.Should().Be("Timestamp");
        query.SortDescending.Should().BeTrue();
    }

    [Fact]
    public void CanSetAllFilters()
    {
        var from = DateTime.UtcNow.AddDays(-7);
        var to = DateTime.UtcNow;

        var query = new SecurityAuditQuery
        {
            FromDate = from,
            ToDate = to,
            UserId = "user-123",
            EventType = "login",
            Severity = SecurityEventSeverity.High,
            Category = SecurityEventCategory.Authentication,
            Source = "UserService",
            IpAddress = "192.168.1.1",
            ResourceType = "User",
            ResourceId = "res-456",
            Tags = new List<string> { "authentication", "sensitive" },
            SearchText = "failed",
            Page = 2,
            PageSize = 50,
            SortBy = "Severity",
            SortDescending = false
        };

        query.FromDate.Should().Be(from);
        query.ToDate.Should().Be(to);
        query.UserId.Should().Be("user-123");
        query.EventType.Should().Be("login");
        query.Severity.Should().Be(SecurityEventSeverity.High);
        query.Category.Should().Be(SecurityEventCategory.Authentication);
        query.Source.Should().Be("UserService");
        query.IpAddress.Should().Be("192.168.1.1");
        query.ResourceType.Should().Be("User");
        query.ResourceId.Should().Be("res-456");
        query.Tags.Should().Contain("authentication");
        query.SearchText.Should().Be("failed");
        query.Page.Should().Be(2);
        query.PageSize.Should().Be(50);
        query.SortBy.Should().Be("Severity");
        query.SortDescending.Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public class IntegrityViolationTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var violation = new IntegrityViolation();

        violation.EventId.Should().Be(string.Empty);
        violation.ViolationType.Should().Be(string.Empty);
        violation.Description.Should().Be(string.Empty);
        violation.EventTimestamp.Should().Be(default(DateTime));
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var ts = DateTime.UtcNow;
        var violation = new IntegrityViolation
        {
            EventId = "evt-123",
            ViolationType = "ChainIntegrity",
            Description = "Hash mismatch detected",
            EventTimestamp = ts
        };

        violation.EventId.Should().Be("evt-123");
        violation.ViolationType.Should().Be("ChainIntegrity");
        violation.Description.Should().Be("Hash mismatch detected");
        violation.EventTimestamp.Should().Be(ts);
    }
}

[Trait("Category", "Unit")]
public class SecurityAuditEventTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var evt = new SecurityAuditEvent();

        evt.Id.Should().NotBeNullOrEmpty();
        evt.EventType.Should().Be(string.Empty);
        evt.Description.Should().Be(string.Empty);
        evt.UserId.Should().BeNull();
        evt.SessionId.Should().BeNull();
        evt.IpAddress.Should().BeNull();
        evt.UserAgent.Should().BeNull();
        evt.RequestId.Should().BeNull();
        evt.Source.Should().Be("Girder");
        evt.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        evt.Severity.Should().Be(SecurityEventSeverity.Information);
        evt.Category.Should().Be(SecurityEventCategory.General);
        evt.ResourceType.Should().BeNull();
        evt.ResourceId.Should().BeNull();
        evt.Action.Should().BeNull();
        evt.Result.Should().BeNull();
        evt.Metadata.Should().NotBeNull();
        evt.Metadata.Should().BeEmpty();
        evt.RiskScore.Should().Be(0);
        evt.Tags.Should().NotBeNull();
        evt.Tags.Should().BeEmpty();
        evt.PreviousEventHash.Should().BeNull();
        evt.EventHash.Should().BeNull();
        evt.Signature.Should().BeNull();
        evt.ComplianceFlags.Should().NotBeNull();
        evt.ComplianceFlags.Should().BeEmpty();
        evt.RetentionDays.Should().Be(2555);
    }

    [Fact]
    public void TwoInstances_HaveDifferentIds()
    {
        var evt1 = new SecurityAuditEvent();
        var evt2 = new SecurityAuditEvent();

        evt1.Id.Should().NotBe(evt2.Id);
    }

    [Fact]
    public void CanSetAllProperties()
    {
        var ts = DateTime.UtcNow;
        var evt = new SecurityAuditEvent
        {
            Id = "custom-id",
            EventType = "UserLogin",
            Description = "User logged in",
            UserId = "user-1",
            SessionId = "session-1",
            IpAddress = "10.0.0.1",
            UserAgent = "Mozilla/5.0",
            RequestId = "req-1",
            Source = "AuthService",
            Timestamp = ts,
            Severity = SecurityEventSeverity.Medium,
            Category = SecurityEventCategory.Authentication,
            ResourceType = "User",
            ResourceId = "res-1",
            Action = "Login",
            Result = "Success",
            RiskScore = 25,
            RetentionDays = 365
        };

        evt.Id.Should().Be("custom-id");
        evt.EventType.Should().Be("UserLogin");
        evt.Description.Should().Be("User logged in");
        evt.Source.Should().Be("AuthService");
        evt.Severity.Should().Be(SecurityEventSeverity.Medium);
        evt.Category.Should().Be(SecurityEventCategory.Authentication);
        evt.RiskScore.Should().Be(25);
        evt.RetentionDays.Should().Be(365);
    }
}

[Trait("Category", "Unit")]
public class AuditIntegrityResultTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var result = new AuditIntegrityResult();

        result.IsIntegrityIntact.Should().BeFalse();
        result.EventsVerified.Should().Be(0);
        result.IntegrityViolations.Should().Be(0);
        result.Violations.Should().NotBeNull();
        result.Violations.Should().BeEmpty();
        result.VerificationTimestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}

[Trait("Category", "Unit")]
public class SecurityAuditReportTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var report = new SecurityAuditReport();

        report.Id.Should().NotBeNullOrEmpty();
        report.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        report.TotalEvents.Should().Be(0);
        report.EventsBySeverity.Should().NotBeNull();
        report.EventsByCategory.Should().NotBeNull();
        report.TopUsersByEventCount.Should().NotBeNull();
        report.TopIpAddressesByEventCount.Should().NotBeNull();
        report.SecurityIncidents.Should().NotBeNull();
        report.ComplianceSummary.Should().NotBeNull();
    }
}

[Trait("Category", "Unit")]
public class SecurityEventEnumsTests
{
    [Fact]
    public void SecurityEventSeverity_ValuesAreOrdered()
    {
        ((int)SecurityEventSeverity.Information).Should().BeLessThan((int)SecurityEventSeverity.Low);
        ((int)SecurityEventSeverity.Low).Should().BeLessThan((int)SecurityEventSeverity.Medium);
        ((int)SecurityEventSeverity.Medium).Should().BeLessThan((int)SecurityEventSeverity.High);
        ((int)SecurityEventSeverity.High).Should().BeLessThan((int)SecurityEventSeverity.Critical);
    }

    [Fact]
    public void SecurityEventCategory_HasExpectedValues()
    {
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.General).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.Authentication).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.Authorization).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.DataAccess).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.DataModification).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.ConfigurationChange).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.SystemEvent).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.SecurityIncident).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.ComplianceEvent).Should().BeTrue();
        Enum.IsDefined(typeof(SecurityEventCategory), SecurityEventCategory.PerformanceEvent).Should().BeTrue();
    }

    [Fact]
    public void AuditExportFormat_HasExpectedValues()
    {
        Enum.IsDefined(typeof(AuditExportFormat), AuditExportFormat.Json).Should().BeTrue();
        Enum.IsDefined(typeof(AuditExportFormat), AuditExportFormat.Csv).Should().BeTrue();
        Enum.IsDefined(typeof(AuditExportFormat), AuditExportFormat.Xml).Should().BeTrue();
        Enum.IsDefined(typeof(AuditExportFormat), AuditExportFormat.Pdf).Should().BeTrue();
    }
}
