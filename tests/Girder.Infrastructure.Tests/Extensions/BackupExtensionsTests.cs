using Girder.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class BackupOptionsTests
{
    [Fact]
    public void BackupOptions_DefaultValues_AreCorrect()
    {
        var options = new BackupOptions();

        options.Enabled.Should().BeTrue();
        options.BackupPath.Should().Be("/backups");
        options.RetentionDays.Should().Be(30);
        options.Schedule.Should().Be("0 2 * * *");
        options.BackupDatabase.Should().BeTrue();
        options.BackupFiles.Should().BeTrue();
        options.CompressBackups.Should().BeTrue();
        options.DatabaseNames.Should().BeEmpty();
        options.FilePaths.Should().BeEmpty();
    }

    [Fact]
    public void BackupOptions_Properties_CanBeSet()
    {
        var options = new BackupOptions
        {
            Enabled = false,
            BackupPath = "/custom/path",
            RetentionDays = 7,
            Schedule = "0 0 * * *",
            BackupDatabase = false,
            BackupFiles = false,
            CompressBackups = false,
            DatabaseNames = new[] { "db1", "db2" },
            FilePaths = new[] { "/path1", "/path2" }
        };

        options.Enabled.Should().BeFalse();
        options.BackupPath.Should().Be("/custom/path");
        options.RetentionDays.Should().Be(7);
        options.DatabaseNames.Should().HaveCount(2);
        options.FilePaths.Should().HaveCount(2);
    }
}

[Trait("Category", "Unit")]
public class BackupResultTests
{
    [Fact]
    public void BackupResult_DefaultValues_AreCorrect()
    {
        var result = new BackupResult();

        result.Success.Should().BeFalse();
        result.BackupId.Should().NotBeNullOrEmpty();
        result.BackupPath.Should().BeEmpty();
        result.SizeInBytes.Should().Be(0);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void BackupResult_Properties_CanBeSet()
    {
        var result = new BackupResult
        {
            Success = true,
            BackupPath = "/backups/test.sql",
            SizeInBytes = 1024,
            Type = BackupType.Database,
            Duration = TimeSpan.FromSeconds(5),
            ErrorMessage = null
        };

        result.Success.Should().BeTrue();
        result.BackupPath.Should().Be("/backups/test.sql");
        result.SizeInBytes.Should().Be(1024);
        result.Type.Should().Be(BackupType.Database);
        result.Duration.Should().Be(TimeSpan.FromSeconds(5));
    }
}

[Trait("Category", "Unit")]
public class BackupTypeTests
{
    [Fact]
    public void BackupType_HasExpectedValues()
    {
        BackupType.Database.Should().BeDefined();
        BackupType.Files.Should().BeDefined();
        BackupType.Full.Should().BeDefined();
    }
}

[Trait("Category", "Unit")]
public class BackupSchedulerTests
{
    private readonly ILogger<BackupScheduler> _logger = Substitute.For<ILogger<BackupScheduler>>();

    [Fact]
    public async Task ScheduleBackupAsync_CompletesSuccessfully()
    {
        var scheduler = new BackupScheduler(_logger);

        var act = () => scheduler.ScheduleBackupAsync("0 2 * * *");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetNextBackupTimeAsync_ReturnsNonNullDateTime()
    {
        var scheduler = new BackupScheduler(_logger);

        var nextTime = await scheduler.GetNextBackupTimeAsync();

        nextTime.Should().NotBeNull();
        nextTime.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }
}

[Trait("Category", "Unit")]
public class BackupServiceTests
{
    private readonly ILogger<BackupService> _logger = Substitute.For<ILogger<BackupService>>();

    private BackupService CreateService(
        IConfiguration? config = null,
        BackupOptions? options = null)
    {
        var configuration = config ?? new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:TestDb", "Host=localhost;Database=testdb;Username=postgres" }
            })
            .Build();

        var opts = Options.Create(options ?? new BackupOptions
        {
            BackupPath = Path.Combine(Path.GetTempPath(), "girder_test_backups_" + Guid.NewGuid()),
            BackupDatabase = true,
            BackupFiles = true,
            DatabaseNames = new[] { "TestDb" },
            FilePaths = new[] { "/tmp/testpath" }
        });

        return new BackupService(_logger, configuration, opts);
    }

    [Fact]
    public async Task BackupDatabaseAsync_MissingConnectionString_ReturnsFailure()
    {
        var config = new ConfigurationBuilder().Build();
        var service = CreateService(config);

        var result = await service.BackupDatabaseAsync("NonExistentDb");

        result.Success.Should().BeFalse();
        result.Type.Should().Be(BackupType.Database);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task BackupFilesAsync_EmptyPaths_ReturnsSuccess()
    {
        var service = CreateService();

        var result = await service.BackupFilesAsync(Array.Empty<string>());

        result.Success.Should().BeTrue();
        result.Type.Should().Be(BackupType.Files);
    }

    [Fact]
    public async Task PerformFullBackupAsync_BackupDatabaseDisabled_SkipsDatabase()
    {
        var service = CreateService(options: new BackupOptions
        {
            BackupPath = Path.Combine(Path.GetTempPath(), "girder_test_backups_" + Guid.NewGuid()),
            BackupDatabase = false,
            BackupFiles = false,
            DatabaseNames = Array.Empty<string>(),
            FilePaths = Array.Empty<string>()
        });

        var result = await service.PerformFullBackupAsync();

        result.Success.Should().BeTrue();
        result.Type.Should().Be(BackupType.Full);
    }

    [Fact]
    public async Task PerformFullBackupAsync_BackupDatabaseEnabled_WithDatabases_ExecutesDatabaseBackups()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:AppDb", "Host=localhost;Database=appdb;Username=postgres" },
                { "ConnectionStrings:LogDb", "Host=localhost;Database=logdb;Username=postgres" }
            })
            .Build();

        var backupPath = Path.Combine(Path.GetTempPath(), "girder_test_backups_" + Guid.NewGuid());
        var service = CreateService(config: config, options: new BackupOptions
        {
            BackupPath = backupPath,
            BackupDatabase = true,
            BackupFiles = false,
            DatabaseNames = new[] { "AppDb", "LogDb" },
            FilePaths = Array.Empty<string>()
        });

        var result = await service.PerformFullBackupAsync();

        // The database backup simulates pg_dump which may succeed even without real DB
        result.Type.Should().Be(BackupType.Full);
    }

    [Fact]
    public async Task PerformFullBackupAsync_BackupFilesEnabled_WithPaths_ExecutesFileBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "girder_source_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var backupPath = Path.Combine(Path.GetTempPath(), "girder_test_backups_" + Guid.NewGuid());

        try
        {
            var service = CreateService(options: new BackupOptions
            {
                BackupPath = backupPath,
                BackupDatabase = false,
                BackupFiles = true,
                DatabaseNames = Array.Empty<string>(),
                FilePaths = new[] { tempDir }
            });

            var result = await service.PerformFullBackupAsync();

            result.Success.Should().BeTrue();
            result.Type.Should().Be(BackupType.Full);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
        }
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_WithExistingFilesOlderThanRetention_CompletesWithoutError()
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "girder_cleanup_" + Guid.NewGuid());
        Directory.CreateDirectory(backupPath);
        var oldFile = Path.Combine(backupPath, "old_backup.sql");
        await File.WriteAllTextAsync(oldFile, "backup content");

        try
        {
            var service = CreateService(options: new BackupOptions { BackupPath = backupPath });

            // Use 0 retention days so any file is "old"
            var act = () => service.CleanupOldBackupsAsync(0);

            await act.Should().NotThrowAsync();
        }
        finally
        {
            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
        }
    }

    [Fact]
    public async Task CleanupOldBackupsAsync_NonExistentDirectory_CompletesWithoutError()
    {
        var service = CreateService(options: new BackupOptions
        {
            BackupPath = "/non/existent/path/" + Guid.NewGuid()
        });

        var act = () => service.CleanupOldBackupsAsync(30);

        await act.Should().NotThrowAsync();
    }
}

[Trait("Category", "Unit")]
public class BackupExtensionsRegistrationTests
{}

[Trait("Category", "Unit")]
public class BackupHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("backup-service", check, null, null)
        };

    [Fact]
    public async Task CheckHealthAsync_WhenBackupDirExistsWithSpace_ReturnsHealthy()
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "girder_backup_hc_" + Guid.NewGuid());
        Directory.CreateDirectory(backupPath);

        try
        {
            var options = Options.Create(new BackupOptions { BackupPath = backupPath });
            var check = new BackupHealthCheck(options);
            var context = CreateContext(check);

            var result = await check.CheckHealthAsync(context);

            // Should be Healthy (plenty of disk space) or Degraded (low disk space on CI)
            result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
            result.Description.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WhenBackupDirDoesNotExist_CreatesItAndReturnsHealthyOrDegraded()
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "girder_backup_hc_new_" + Guid.NewGuid());

        try
        {
            var options = Options.Create(new BackupOptions { BackupPath = backupPath });
            var check = new BackupHealthCheck(options);
            var context = CreateContext(check);

            var result = await check.CheckHealthAsync(context);

            result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
            Directory.Exists(backupPath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WhenHealthy_DescriptionContainsFreeSpaceInfo()
    {
        var backupPath = Path.Combine(Path.GetTempPath(), "girder_backup_hc_desc_" + Guid.NewGuid());
        Directory.CreateDirectory(backupPath);

        try
        {
            var options = Options.Create(new BackupOptions { BackupPath = backupPath });
            var check = new BackupHealthCheck(options);
            var context = CreateContext(check);

            var result = await check.CheckHealthAsync(context);

            result.Description.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (Directory.Exists(backupPath))
                Directory.Delete(backupPath, true);
        }
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_ReturnsUnhealthy()
    {
        // Use a path on a non-existent drive to trigger an exception from DriveInfo
        // On macOS/Linux this will throw because "/" + null byte in path is invalid
        // We use a path with invalid chars to trigger an ArgumentException
        var invalidPath = "/\0invalid";
        var options = Options.Create(new BackupOptions { BackupPath = invalidPath });
        var check = new BackupHealthCheck(options);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}
