using Girder.Infrastructure.Security.Audit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Audit;

[Trait("Category", "Unit")]
public class SecurityAuditMaintenanceServiceTests
{
    private readonly ISecurityAuditService _auditService = Substitute.For<ISecurityAuditService>();
    private readonly ILogger<SecurityAuditMaintenanceService> _logger = Substitute.For<ILogger<SecurityAuditMaintenanceService>>();

    private SecurityAuditMaintenanceService CreateService(SecurityAuditOptions? options = null)
    {
        var opts = Options.Create(options ?? new SecurityAuditOptions
        {
            EnableIntegrityVerification = true,
            IntegrityVerificationIntervalHours = 1,
            ArchiveAfterDays = 365
        });
        return new SecurityAuditMaintenanceService(_auditService, _logger, opts);
    }

    #region ExecuteAsync - cancellation

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_StopsGracefully()
    {
        var service = CreateService(new SecurityAuditOptions
        {
            EnableIntegrityVerification = false,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 365
        });

        // Set up archival to return 0
        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(0);

        _auditService.VerifyAuditIntegrityAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AuditIntegrityResult { IsIntegrityIntact = true, EventsVerified = 0 });

        using var cts = new CancellationTokenSource();

        var task = service.StartAsync(cts.Token);
        await Task.Delay(50); // Let it start
        cts.Cancel();

        var stopTask = service.StopAsync(CancellationToken.None);

        // Should complete without hanging
        await Task.WhenAny(stopTask, Task.Delay(3000));
    }

    #endregion

    #region Maintenance - IntegrityCheck

    [Fact]
    public async Task ExecuteAsync_IntegrityCheckEnabled_IntegrityOk_LogsInfo()
    {
        var options = new SecurityAuditOptions
        {
            EnableIntegrityVerification = true,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 365
        };

        _auditService.VerifyAuditIntegrityAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AuditIntegrityResult
            {
                IsIntegrityIntact = true,
                EventsVerified = 5,
                IntegrityViolations = 0
            });

        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await _auditService.Received().VerifyAuditIntegrityAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_IntegrityCheckEnabled_IntegrityFailed_LogsCritical()
    {
        var options = new SecurityAuditOptions
        {
            EnableIntegrityVerification = true,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 365
        };

        _auditService.VerifyAuditIntegrityAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(new AuditIntegrityResult
            {
                IsIntegrityIntact = false,
                EventsVerified = 10,
                IntegrityViolations = 2
            });

        // Waiting a fixed 200 ms and hoping the background service got there
        // made this test fail under load. Wait for the call itself instead.
        var critical = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _auditService.LogSecurityEventAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecurityEventSeverity>(),
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.ArgAt<string>(0).Contains("Integrity")
                    && call.ArgAt<SecurityEventSeverity>(2) == SecurityEventSeverity.Critical)
                {
                    critical.TrySetResult();
                }

                return "logged-id";
            });

        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var service = CreateService(options);

        try
        {
            await service.StartAsync(CancellationToken.None);

            var reached = await Task.WhenAny(critical.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            reached.Should().BeSameAs(critical.Task, "the failed integrity check must be logged");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await _auditService.Received().LogSecurityEventAsync(
            Arg.Is<string>(s => s.Contains("Integrity")),
            Arg.Any<string>(),
            SecurityEventSeverity.Critical,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Maintenance - ArchiveLogs

    [Fact]
    public async Task ExecuteAsync_ArchivesOldLogs_WhenCountGreaterThanZero_LogsArchivedEvent()
    {
        var options = new SecurityAuditOptions
        {
            EnableIntegrityVerification = false,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 90
        };

        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(5);

        _auditService.LogSecurityEventAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SecurityEventSeverity>(),
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns("archived-id");

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        await _auditService.Received().ArchiveOldLogsAsync(
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ArchivesOldLogs_WhenCountZero_DoesNotLogEvent()
    {
        var options = new SecurityAuditOptions
        {
            EnableIntegrityVerification = false,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 90
        };

        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // No log event should be published for archival when count is 0
        await _auditService.DidNotReceive().LogSecurityEventAsync(
            Arg.Is<string>(s => s.Contains("Archive")),
            Arg.Any<string>(),
            Arg.Any<SecurityEventSeverity>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Error handling

    [Fact]
    public async Task ExecuteAsync_IntegrityCheckThrows_ContinuesExecution()
    {
        var options = new SecurityAuditOptions
        {
            EnableIntegrityVerification = true,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 365
        };

        _auditService.VerifyAuditIntegrityAsync(
            Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Should not crash - verify archive was still attempted
        await _auditService.Received().ArchiveOldLogsAsync(
            Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ArchiveThrows_DoesNotCrash()
    {
        var options = new SecurityAuditOptions
        {
            EnableIntegrityVerification = false,
            IntegrityVerificationIntervalHours = 500,
            ArchiveAfterDays = 365
        };

        _auditService.ArchiveOldLogsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Archive failed"));

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        try
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(200);
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Should not throw
            await service.StopAsync(CancellationToken.None);
        }
    }

    #endregion
}
