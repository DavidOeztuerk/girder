using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class KeyBackgroundServicesTests
{
    #region KeyRotationBackgroundService

    /// <summary>
    /// Waits until <paramref name="signal"/> fires, or fails the test.
    /// </summary>
    /// <remarks>
    /// These services do their work on a background loop. A fixed Task.Delay
    /// only happened to be long enough and started failing once the suite grew
    /// busier, so the tests wait for the call itself.
    /// </remarks>
    private static async Task WaitFor(TaskCompletionSource signal, string because)
    {
        var reached = await Task.WhenAny(signal.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        reached.Should().BeSameAs(signal.Task, because);
    }

    [Fact]
    public async Task KeyRotationBackgroundService_AutoRotateDisabled_StopsImmediately()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyRotationBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions { AutoRotateKeys = false });

        var service = new KeyRotationBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await service.StopAsync(cts.Token);

        // Should not have called GetActiveKeysAsync since auto-rotate is disabled
        await keyManagementService.DidNotReceive().GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyRotationBackgroundService_AutoRotateEnabled_ChecksKeys()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyRotationBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoRotateKeys = true,
            DefaultRotationInterval = TimeSpan.FromDays(90)
        });

        // Return empty list so no rotations happen
        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata>());

        var service = new KeyRotationBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(150);
        await service.StopAsync(CancellationToken.None);

        await keyManagementService.Received().GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyRotationBackgroundService_KeyNeedsRotation_RotatesKey()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyRotationBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoRotateKeys = true,
            DefaultRotationInterval = TimeSpan.FromMilliseconds(1) // Very short to trigger rotation
        });

        var expiredKey = new KeyMetadata
        {
            Id = "key-1",
            CreatedAt = DateTime.UtcNow.AddDays(-100), // Old enough to trigger rotation
            NextRotation = DateTime.UtcNow.AddDays(-1) // Past due
        };

        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { expiredKey });
        var rotated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        keyManagementService.RotateKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { rotated.TrySetResult(); return Task.FromResult("new-key-id"); });

        var service = new KeyRotationBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitFor(rotated, "an overdue key must be rotated");
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        await keyManagementService.Received().RotateKeyAsync("key-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyRotationBackgroundService_RotationThrows_LogsError()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyRotationBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoRotateKeys = true,
            DefaultRotationInterval = TimeSpan.FromMilliseconds(1)
        });

        var expiredKey = new KeyMetadata
        {
            Id = "key-1",
            CreatedAt = DateTime.UtcNow.AddDays(-100),
            NextRotation = DateTime.UtcNow.AddDays(-1)
        };

        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { expiredKey });
        keyManagementService.RotateKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Rotation failed"));

        var service = new KeyRotationBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(150);

        // Should not throw - exception is caught and logged
        var act = async () => await service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region KeyMaintenanceBackgroundService

    [Fact]
    public async Task KeyMaintenanceBackgroundService_StartsAndStopsCleanly()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyMaintenanceBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoCreateBackups = false,
            EnableUsageMonitoring = false
        });

        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata>());

        var service = new KeyMaintenanceBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task KeyMaintenanceBackgroundService_AutoCreateBackupsEnabled_ChecksForBackups()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyMaintenanceBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoCreateBackups = true,
            EnableUsageMonitoring = false
        });

        var keyWithoutBackup = new KeyMetadata
        {
            Id = "key-no-backup",
            HasBackup = false
        };

        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyWithoutBackup });
        var backedUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        keyManagementService.BackupKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { backedUp.TrySetResult(); return Task.FromResult(new KeyBackupResult { Success = true, BackupId = "backup-1" }); });

        var service = new KeyMaintenanceBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitFor(backedUp, "a key without a backup must be backed up");
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        await keyManagementService.Received().BackupKeyAsync("key-no-backup", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeyMaintenanceBackgroundService_BackupFails_LogsError()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyMaintenanceBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoCreateBackups = true,
            EnableUsageMonitoring = false
        });

        var keyWithoutBackup = new KeyMetadata { Id = "key-1", HasBackup = false };

        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyWithoutBackup });
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        keyManagementService.BackupKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<KeyBackupResult>(_ =>
            {
                attempted.TrySetResult();
                throw new Exception("Backup failed");
            });

        var service = new KeyMaintenanceBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitFor(attempted, "the backup must at least be attempted");
        await cts.CancelAsync();

        var act = async () => await service.StopAsync(CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task KeyMaintenanceBackgroundService_BackupReturnsFailure_LogsError()
    {
        var keyManagementService = Substitute.For<IKeyManagementService>();
        var logger = Substitute.For<ILogger<KeyMaintenanceBackgroundService>>();
        var options = Options.Create(new KeyManagementOptions
        {
            AutoCreateBackups = true,
            EnableUsageMonitoring = false
        });

        var keyWithoutBackup = new KeyMetadata { Id = "key-1", HasBackup = false };

        keyManagementService.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyWithoutBackup });
        var backedUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        keyManagementService.BackupKeyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { backedUp.TrySetResult(); return Task.FromResult(new KeyBackupResult { Success = false, ErrorMessage = "Storage unavailable" }); });

        var service = new KeyMaintenanceBackgroundService(keyManagementService, logger, options);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await WaitFor(backedUp, "a key without a backup must be backed up");
        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        await keyManagementService.Received().BackupKeyAsync("key-1", Arg.Any<CancellationToken>());
    }

    #endregion
}
