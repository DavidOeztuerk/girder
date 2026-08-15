using Girder.Infrastructure.Extensions;
using Serilog;
using Serilog.Events;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class LoggerExtensionsTests
{
    private static (Serilog.ILogger logger, List<LogEvent> events) CreateTestLogger()
    {
        var events = new List<LogEvent>();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(new ListSink(events))
            .CreateLogger();
        return (logger, events);
    }

    /// <summary>
    /// Minimal Serilog sink that collects events into a list.
    /// </summary>
    private class ListSink : Serilog.Core.ILogEventSink
    {
        private readonly List<LogEvent> _events;
        public ListSink(List<LogEvent> events) => _events = events;
        public void Emit(LogEvent logEvent) => _events.Add(logEvent);
    }

    #region LogUserAction

    [Fact]
    public void LogUserAction_LogsAtInformationLevel()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogUserAction("user-123", "Login");

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void LogUserAction_IncludesUserIdAndAction()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogUserAction("user-456", "CreateInvoice");

        var evt = events.Single();
        evt.Properties.Should().ContainKey("UserId");
        evt.Properties["UserId"].ToString().Should().Contain("user-456");
        evt.Properties.Should().ContainKey("Action");
        evt.Properties["Action"].ToString().Should().Contain("CreateInvoice");
    }

    [Fact]
    public void LogUserAction_WithNullDetails_DoesNotThrow()
    {
        var (logger, events) = CreateTestLogger();

        var act = () => logger.LogUserAction("user-1", "Test", null);

        act.Should().NotThrow();
        events.Should().ContainSingle();
    }

    [Fact]
    public void LogUserAction_WithDetails_IncludesDetailsProperty()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogUserAction("user-1", "Update", new { Field = "name" });

        var evt = events.Single();
        evt.Properties.Should().ContainKey("Details");
    }

    #endregion

    #region LogBusinessEvent

    [Fact]
    public void LogBusinessEvent_LogsAtInformationLevel()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogBusinessEvent("OrderPlaced", new { OrderId = "o-1" });

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void LogBusinessEvent_IncludesEventNameAndData()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogBusinessEvent("InvoiceIssued", new { InvoiceId = "i-1" });

        var evt = events.Single();
        evt.Properties.Should().ContainKey("EventName");
        evt.Properties["EventName"].ToString().Should().Contain("InvoiceIssued");
        evt.Properties.Should().ContainKey("EventData");
    }

    #endregion

    #region LogPerformanceMetric

    [Fact]
    public void LogPerformanceMetric_FastOperation_LogsAtInformationLevel()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogPerformanceMetric("DbQuery", 200);

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void LogPerformanceMetric_SlowOperation_LogsAtWarningLevel()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogPerformanceMetric("SlowQuery", 5001);

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Warning);
    }

    [Fact]
    public void LogPerformanceMetric_ExactlyAt5000ms_LogsAsInformation()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogPerformanceMetric("BorderlineQuery", 5000);

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Information);
    }

    [Fact]
    public void LogPerformanceMetric_IncludesOperationAndElapsedMs()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogPerformanceMetric("FetchUsers", 150);

        var evt = events.Single();
        evt.Properties.Should().ContainKey("Operation");
        evt.Properties["Operation"].ToString().Should().Contain("FetchUsers");
        evt.Properties.Should().ContainKey("ElapsedMs");
    }

    [Fact]
    public void LogPerformanceMetric_SlowWithContext_IncludesContext()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogPerformanceMetric("HeavyOp", 6000, new { Table = "users" });

        var evt = events.Single();
        evt.Level.Should().Be(LogEventLevel.Warning);
        evt.Properties.Should().ContainKey("Context");
    }

    #endregion

    #region LogSecurityEvent

    [Fact]
    public void LogSecurityEvent_LogsAtWarningLevel()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogSecurityEvent("UnauthorizedAccess");

        events.Should().ContainSingle();
        events[0].Level.Should().Be(LogEventLevel.Warning);
    }

    [Fact]
    public void LogSecurityEvent_WithUserId_IncludesUserId()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogSecurityEvent("FailedLogin", "user-999");

        var evt = events.Single();
        evt.Properties.Should().ContainKey("UserId");
        evt.Properties["UserId"].ToString().Should().Contain("user-999");
    }

    [Fact]
    public void LogSecurityEvent_WithNullUserId_UsesAnonymous()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogSecurityEvent("SuspiciousActivity");

        var evt = events.Single();
        evt.Properties.Should().ContainKey("UserId");
        evt.Properties["UserId"].ToString().Should().Contain("Anonymous");
    }

    [Fact]
    public void LogSecurityEvent_IncludesEventType()
    {
        var (logger, events) = CreateTestLogger();

        logger.LogSecurityEvent("BruteForceDetected", "user-1", new { Attempts = 10 });

        var evt = events.Single();
        evt.Properties.Should().ContainKey("EventType");
        evt.Properties["EventType"].ToString().Should().Contain("BruteForceDetected");
    }

    #endregion
}
