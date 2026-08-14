using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecretRotationServiceTopUpTests
{
    private readonly ISecretManager _secretManager = Substitute.For<ISecretManager>();
    private readonly ILogger<SecretRotationService> _logger = Substitute.For<ILogger<SecretRotationService>>();

    private SecretRotationService CreateService(SecretRotationOptions options)
    {
        return new SecretRotationService(_secretManager, _logger, Options.Create(options));
    }

    [Fact]
    public async Task ExecuteAsync_WithManyVersions_CleansOldOnes()
    {
        // Arrange: KeepOldSecretsCount=1, so we keep 2 total (1+1)
        // But we have 5 versions — service should log about cleanup for 3 extras
        var options = new SecretRotationOptions
        {
            EnableRotation = true,
            RotationIntervalHours = 1,
            KeepOldSecretsCount = 1,
            SecretsToRotate = ["CleanupSecret"]
        };

        // Active version is recent (no rotation needed)
        var recentVersion = new SecretVersion
        {
            Name = "CleanupSecret",
            Version = 5,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        // Many older versions
        var oldVersions = Enumerable.Range(1, 4).Select(i => new SecretVersion
        {
            Name = "CleanupSecret",
            Version = i,
            CreatedAt = DateTime.UtcNow.AddDays(-i),
            IsActive = false
        }).ToList();

        var allVersions = new List<SecretVersion> { recentVersion }.Concat(oldVersions);

        _secretManager.GetSecretHistoryAsync("CleanupSecret", Arg.Any<CancellationToken>())
            .Returns(allVersions);

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(150);
        await service.StopAsync(CancellationToken.None);

        // Should NOT have rotated (recent version)
        await _secretManager.DidNotReceive().RotateSecretAsync("CleanupSecret", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithExpiredSecret_RotatesAndContinues()
    {
        var options = new SecretRotationOptions
        {
            EnableRotation = true,
            RotationIntervalHours = 1,
            KeepOldSecretsCount = 2,
            SecretsToRotate = ["ExpiredSecret", "FreshSecret"]
        };

        var expiredVersion = new SecretVersion
        {
            Name = "ExpiredSecret",
            Version = 1,
            CreatedAt = DateTime.UtcNow.AddHours(-2), // older than interval
            IsActive = true
        };

        var freshVersion = new SecretVersion
        {
            Name = "FreshSecret",
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _secretManager.GetSecretHistoryAsync("ExpiredSecret", Arg.Any<CancellationToken>())
            .Returns(new[] { expiredVersion });
        _secretManager.GetSecretHistoryAsync("FreshSecret", Arg.Any<CancellationToken>())
            .Returns(new[] { freshVersion });
        _secretManager.RotateSecretAsync("ExpiredSecret", Arg.Any<CancellationToken>())
            .Returns("new-value");

        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(150);
        await service.StopAsync(CancellationToken.None);

        await _secretManager.Received().RotateSecretAsync("ExpiredSecret", Arg.Any<CancellationToken>());
        await _secretManager.DidNotReceive().RotateSecretAsync("FreshSecret", Arg.Any<CancellationToken>());
    }
}
