using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecurityAuditLoggerTests
{
    private readonly SecurityAuditLogger _logger;

    public SecurityAuditLoggerTests()
    {
        var loggerMock = Substitute.For<ILogger<SecurityAuditLogger>>();
        _logger = new SecurityAuditLogger(loggerMock);
    }

    [Fact]
    public async Task LogSecurityEventAsync_DoesNotThrow()
    {
        var auditEvent = new SecurityAuditEvent
        {
            EventType = "LoginSuccess",
            Description = "User logged in",
            UserId = "user-1",
            Severity = SecurityEventSeverity.Information
        };

        var act = () => _logger.LogSecurityEventAsync(auditEvent);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetSecurityEventsAsync_ReturnsEmpty()
    {
        var result = await _logger.GetSecurityEventsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public void SecurityAuditEvent_DefaultValues()
    {
        var evt = new SecurityAuditEvent();

        evt.Id.Should().NotBeNullOrEmpty();
        evt.EventType.Should().BeEmpty();
        evt.Description.Should().BeEmpty();
        evt.Severity.Should().Be(SecurityEventSeverity.Information);
        evt.Source.Should().Be("Girder");
        evt.Metadata.Should().NotBeNull();
        evt.Metadata.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SecurityEventSeverity.Information)]
    [InlineData(SecurityEventSeverity.Warning)]
    [InlineData(SecurityEventSeverity.Error)]
    [InlineData(SecurityEventSeverity.Critical)]
    public void SecurityEventSeverity_AllValuesAreValid(SecurityEventSeverity severity)
    {
        var evt = new SecurityAuditEvent { Severity = severity };
        evt.Severity.Should().Be(severity);
    }
}
