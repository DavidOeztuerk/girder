using Girder.Infrastructure.Security.Audit;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security.Audit;

[Trait("Category", "Unit")]
public class SecurityAuditServiceTests
{
    private readonly SecurityAuditService _sut;
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly ILogger<SecurityAuditService> _logger = Substitute.For<ILogger<SecurityAuditService>>();
    private readonly byte[] _signingKey = new byte[32];

    public SecurityAuditServiceTests()
    {
        Array.Fill(_signingKey, (byte)0xAB);
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _sut = new SecurityAuditService(_connectionMultiplexer, _logger, _signingKey);
    }

    #region LogSecurityEventAsync

    [Fact]
    public async Task LogSecurityEventAsync_ValidEvent_ReturnsEventId()
    {
        var auditEvent = CreateAuditEvent();

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("prev-hash")));

        var result = await _sut.LogSecurityEventAsync(auditEvent);

        result.Should().NotBeNullOrEmpty();
        result.Should().Be(auditEvent.Id);
    }

    [Fact]
    public async Task LogSecurityEventAsync_SetsRiskScoreIfZero()
    {
        var auditEvent = CreateAuditEvent();
        auditEvent.RiskScore = 0;
        auditEvent.Severity = SecurityEventSeverity.Critical;

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(auditEvent);

        auditEvent.RiskScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task LogSecurityEventAsync_PreservesExistingRiskScore()
    {
        var auditEvent = CreateAuditEvent();
        auditEvent.RiskScore = 42;

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(auditEvent);

        auditEvent.RiskScore.Should().Be(42);
    }

    [Fact]
    public async Task LogSecurityEventAsync_SetsEventHashAndSignature()
    {
        var auditEvent = CreateAuditEvent();

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(auditEvent);

        auditEvent.EventHash.Should().NotBeNullOrEmpty();
        auditEvent.Signature.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LogSecurityEventAsync_SetsKeyExpiration()
    {
        var auditEvent = CreateAuditEvent();
        auditEvent.RetentionDays = 30;

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(auditEvent);

        await _database.Received(1).KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<ExpireWhen>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task LogSecurityEventAsync_RedisError_ThrowsException()
    {
        var auditEvent = CreateAuditEvent();

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection failed"));

        var act = () => _sut.LogSecurityEventAsync(auditEvent);

        await act.Should().ThrowAsync<RedisException>();
    }

    [Fact]
    public async Task LogSecurityEventAsync_StringOverload_CreatesEvent()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        var result = await _sut.LogSecurityEventAsync(
            "login-attempt",
            "User login attempt",
            SecurityEventSeverity.Information);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LogSecurityEventAsync_WithAdditionalData_PopulatesMetadata()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        var result = await _sut.LogSecurityEventAsync(
            "login-attempt",
            "Login",
            SecurityEventSeverity.Low,
            new { Username = "testuser" });

        result.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region GetSecurityEventsAsync

    [Fact]
    public async Task GetSecurityEventsAsync_NoEvents_ReturnsEmpty()
    {
        var query = new SecurityAuditQuery();

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.GetSecurityEventsAsync(query);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSecurityEventsAsync_RedisError_ReturnsEmpty()
    {
        var query = new SecurityAuditQuery();

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection failed"));

        var result = await _sut.GetSecurityEventsAsync(query);

        result.Should().BeEmpty();
    }

    #endregion

    #region VerifyAuditIntegrityAsync

    [Fact]
    public async Task VerifyAuditIntegrityAsync_NoEvents_ReturnsIntact()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.VerifyAuditIntegrityAsync();

        result.IsIntegrityIntact.Should().BeTrue();
        result.EventsVerified.Should().Be(0);
    }

    [Fact]
    public async Task VerifyAuditIntegrityAsync_NoEvents_ReturnsIntactWithZeroViolations()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.VerifyAuditIntegrityAsync();

        result.IsIntegrityIntact.Should().BeTrue();
        result.IntegrityViolations.Should().Be(0);
    }

    #endregion

    #region ExportAuditLogsAsync

    [Fact]
    public async Task ExportAuditLogsAsync_JsonFormat_ReturnsBytes()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.ExportAuditLogsAsync(new SecurityAuditQuery(), AuditExportFormat.Json);

        result.Should().NotBeNull();
        result.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExportAuditLogsAsync_CsvFormat_ReturnsBytes()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.ExportAuditLogsAsync(new SecurityAuditQuery(), AuditExportFormat.Csv);

        result.Should().NotBeNull();
        var csv = System.Text.Encoding.UTF8.GetString(result);
        csv.Should().Contain("Id,EventType");
    }

    [Fact]
    public async Task ExportAuditLogsAsync_XmlFormat_ReturnsBytes()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.ExportAuditLogsAsync(new SecurityAuditQuery(), AuditExportFormat.Xml);

        result.Should().NotBeNull();
        var xml = System.Text.Encoding.UTF8.GetString(result);
        xml.Should().Contain("SecurityAuditEvents");
    }

    [Fact]
    public async Task ExportAuditLogsAsync_UnsupportedFormat_ThrowsArgumentException()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var act = () => _sut.ExportAuditLogsAsync(new SecurityAuditQuery(), AuditExportFormat.Pdf);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region ArchiveOldLogsAsync

    [Fact]
    public async Task ArchiveOldLogsAsync_NoEvents_ReturnsZero()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.ArchiveOldLogsAsync(DateTime.UtcNow.AddDays(-30));

        result.Should().Be(0);
    }

    [Fact]
    public async Task ArchiveOldLogsAsync_WithEvents_ArchivesAndReturnsCount()
    {
        var eventId = "event-1";
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { eventId });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("{\"id\":\"event-1\"}"));

        var result = await _sut.ArchiveOldLogsAsync(DateTime.UtcNow.AddDays(-30));

        result.Should().Be(1);
        await _database.Received(1).KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ArchiveOldLogsAsync_RedisError_Throws()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.ArchiveOldLogsAsync(DateTime.UtcNow);

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region GetAuditStatisticsAsync

    [Fact]
    public async Task GetAuditStatisticsAsync_NoEvents_ReturnsZeroTotal()
    {
        _database.SortedSetLengthAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<CommandFlags>())
            .Returns(0L);

        var result = await _sut.GetAuditStatisticsAsync();

        result.TotalEvents.Should().Be(0);
    }

    [Fact]
    public async Task GetAuditStatisticsAsync_RedisError_Throws()
    {
        _database.SortedSetLengthAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.GetAuditStatisticsAsync();

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region GenerateAuditReportAsync

    [Fact]
    public async Task GenerateAuditReportAsync_NoEvents_ReturnsEmptyReport()
    {
        var query = new SecurityAuditQuery { FromDate = DateTime.UtcNow.AddDays(-7) };

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.GenerateAuditReportAsync(query);

        result.TotalEvents.Should().Be(0);
        result.EventsBySeverity.Should().BeEmpty();
        result.EventsByCategory.Should().BeEmpty();
    }

    #endregion

    private static SecurityAuditEvent CreateAuditEvent(
        SecurityEventSeverity severity = SecurityEventSeverity.Information,
        SecurityEventCategory category = SecurityEventCategory.General)
    {
        return new SecurityAuditEvent
        {
            EventType = "test-event",
            Description = "Test event description",
            Severity = severity,
            Category = category,
            UserId = "user-123",
            IpAddress = "127.0.0.1"
        };
    }

    private static string CreateAuditEventJson(
        string id,
        SecurityEventSeverity severity,
        SecurityEventCategory category)
    {
        var evt = new SecurityAuditEvent
        {
            Id = id,
            EventType = $"test-{id}",
            Description = "Test event",
            Severity = severity,
            Category = category,
            UserId = "user-test",
            IpAddress = "127.0.0.1",
            Timestamp = DateTime.UtcNow,
            EventHash = "test-hash",
            Signature = "test-sig"
        };
        return JsonSerializer.Serialize(evt, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    #region Additional Tests

    [Fact]
    public async Task GetSecurityEventsAsync_WithMatchingEvents_ReturnsDeserializedEvents()
    {
        var eventId = "event-abc-123";
        var auditEvent = new SecurityAuditEvent
        {
            Id = eventId,
            EventType = "test-login",
            Description = "User logged in",
            Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.Authentication,
            UserId = "user-123",
            IpAddress = "10.0.0.1"
        };
        var json = JsonSerializer.Serialize(auditEvent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { eventId });

        _database.StringGetAsync(
            Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var query = new SecurityAuditQuery { UserId = "user-123" };
        var result = await _sut.GetSecurityEventsAsync(query);

        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetSecurityEventsAsync_WithInvalidJson_SkipsEvent()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "bad-event-id" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("{ invalid json }"));

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSecurityEventsAsync_EventNotInStorage_SkipsIt()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "missing-event-id" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetSecurityEventsAsync(new SecurityAuditQuery());

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAuditStatisticsAsync_WithEventsCount_ReturnsCorrectTotal()
    {
        _database.SortedSetLengthAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<CommandFlags>())
            .Returns(42L);

        var result = await _sut.GetAuditStatisticsAsync();

        result.TotalEvents.Should().Be(42);
    }

    [Fact]
    public async Task GetAuditStatisticsAsync_ReturnsNonNullResult()
    {
        _database.SortedSetLengthAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<CommandFlags>())
            .Returns(0L);

        var result = await _sut.GetAuditStatisticsAsync();

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateAuditReportAsync_WithEvents_ReturnsGroupedStats()
    {
        var event1 = CreateAuditEventJson("event-1", SecurityEventSeverity.Critical, SecurityEventCategory.Authentication);
        var event2 = CreateAuditEventJson("event-2", SecurityEventSeverity.High, SecurityEventCategory.Authorization);

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "event-1", "event-2" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<RedisKey>(0).ToString();
                if (key.Contains("event-1")) return new RedisValue(event1);
                if (key.Contains("event-2")) return new RedisValue(event2);
                return RedisValue.Null;
            });

        var query = new SecurityAuditQuery { FromDate = DateTime.UtcNow.AddDays(-1) };
        var result = await _sut.GenerateAuditReportAsync(query);

        result.Should().NotBeNull();
        result.TotalEvents.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateAuditReportAsync_ReturnsReport()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.GenerateAuditReportAsync(new SecurityAuditQuery());

        result.Should().NotBeNull();
        result.GeneratedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task VerifyAuditIntegrityAsync_WithEvents_ReturnsResult()
    {
        var auditEvent = new SecurityAuditEvent
        {
            Id = "event-verify-1",
            EventType = "test",
            Description = "test",
            EventHash = "hash-value",
            Signature = "sig-value",
            PreviousEventHash = string.Empty,
            Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.General
        };
        var json = JsonSerializer.Serialize(auditEvent, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "event-verify-1" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.VerifyAuditIntegrityAsync();

        result.Should().NotBeNull();
        result.EventsVerified.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task VerifyAuditIntegrityAsync_WithTamperedHash_ReportsViolations()
    {
        var auditEvent1 = new SecurityAuditEvent
        {
            Id = "event-tampered-1",
            EventType = "test",
            Description = "test",
            EventHash = "tampered-hash",
            Signature = "sig",
            PreviousEventHash = "previous-hash-123",
            Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.General
        };
        var auditEvent2 = new SecurityAuditEvent
        {
            Id = "event-tampered-2",
            EventType = "test",
            Description = "test",
            EventHash = "another-hash",
            Signature = "sig2",
            PreviousEventHash = "wrong-previous-hash",
            Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.General
        };

        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "event-tampered-1", "event-tampered-2" });

        var camelCaseOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<RedisKey>(0).ToString();
                if (key.Contains("event-tampered-1")) return new RedisValue(JsonSerializer.Serialize(auditEvent1, camelCaseOptions));
                if (key.Contains("event-tampered-2")) return new RedisValue(JsonSerializer.Serialize(auditEvent2, camelCaseOptions));
                return RedisValue.Null;
            });

        var result = await _sut.VerifyAuditIntegrityAsync();

        result.Should().NotBeNull();
        // Chain mismatch should be detected (event-2's previous hash doesn't match event-1's hash)
        result.IntegrityViolations.Should().BeGreaterThan(0);
        result.IsIntegrityIntact.Should().BeFalse();
    }

    [Fact]
    public async Task LogSecurityEventAsync_CriticalAuthFailure_GetsHighRiskScore()
    {
        var auditEvent = new SecurityAuditEvent
        {
            EventType = "auth-failure",
            Description = "Multiple auth failures",
            Severity = SecurityEventSeverity.Critical,
            Category = SecurityEventCategory.Authentication,
            RiskScore = 0
        };

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(auditEvent);

        auditEvent.RiskScore.Should().BeGreaterThan(0);
        auditEvent.RiskScore.Should().BeLessThanOrEqualTo(100);

    }

    [Fact]
    public async Task LogSecurityEventAsync_LowSeverityGeneral_GetsLowerRiskScore()
    {
        var criticalEvent = new SecurityAuditEvent
        {
            EventType = "critical-event",
            Severity = SecurityEventSeverity.Critical,
            Category = SecurityEventCategory.Authentication,
            RiskScore = 0
        };
        var lowEvent = new SecurityAuditEvent
        {
            EventType = "low-event",
            Severity = SecurityEventSeverity.Low,
            Category = SecurityEventCategory.General,
            RiskScore = 0
        };

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(criticalEvent);
        await _sut.LogSecurityEventAsync(lowEvent);

        criticalEvent.RiskScore.Should().BeGreaterThan(lowEvent.RiskScore);
    }

    [Fact]
    public async Task LogSecurityEventAsync_WithRetentionDays_SetsExpiry()
    {
        var auditEvent = new SecurityAuditEvent
        {
            EventType = "test",
            Description = "test",
            Severity = SecurityEventSeverity.Information,
            Category = SecurityEventCategory.General,
            RetentionDays = 90
        };

        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        await _sut.LogSecurityEventAsync(auditEvent);

        await _database.Received(1).KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Is<TimeSpan>(ts => ts.TotalDays >= 89 && ts.TotalDays <= 91),
            Arg.Any<ExpireWhen>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task LogSecurityEventAsync_StringOverload_WithAdditionalData_LogsSuccessfully()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]>(),
            Arg.Any<RedisValue[]>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(new RedisValue("")));

        var result = await _sut.LogSecurityEventAsync(
            "user-login",
            "User login attempt",
            SecurityEventSeverity.Information,
            new { Username = "testuser", IpAddress = "192.168.1.1", Timestamp = DateTime.UtcNow });

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ArchiveOldLogsAsync_WithMultipleEvents_ArchivesAll()
    {
        var eventIds = new[] { "evt-1", "evt-2", "evt-3" };
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(eventIds.Select(id => new RedisValue(id)).ToArray());

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<RedisKey>(0).ToString();
                foreach (var id in eventIds)
                {
                    if (key.Contains(id))
                        return new RedisValue($"{{\"id\":\"{id}\"}}");
                }
                return RedisValue.Null;
            });

        var result = await _sut.ArchiveOldLogsAsync(DateTime.UtcNow.AddDays(-30));

        result.Should().Be(3);
    }

    [Fact]
    public async Task ArchiveOldLogsAsync_WithNullEventData_SkipsNull()
    {
        _database.SortedSetRangeByScoreAsync(
            Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(),
            Arg.Any<Exclude>(), Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "event-null-1" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.ArchiveOldLogsAsync(DateTime.UtcNow);

        result.Should().Be(0);
    }

    #endregion
}
