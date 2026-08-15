using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecretRotationServiceTests
{
    private readonly ISecretManager _secretManager;
    private readonly ILogger<SecretRotationService> _logger;

    public SecretRotationServiceTests()
    {
        _secretManager = Substitute.For<ISecretManager>();
        _logger = Substitute.For<ILogger<SecretRotationService>>();
    }

    private SecretRotationService CreateService(SecretRotationOptions? options = null)
    {
        var opts = Options.Create(options ?? new SecretRotationOptions
        {
            EnableRotation = true,
            RotationIntervalHours = 1,
            KeepOldSecretsCount = 2,
            SecretsToRotate = ["TestSecret"]
        });
        return new SecretRotationService(_secretManager, _logger, opts);
    }

    [Fact]
    public async Task ExecuteAsync_RotationDisabled_ReturnsImmediately()
    {
        var options = new SecretRotationOptions { EnableRotation = false };
        var service = CreateService(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        await service.StopAsync(CancellationToken.None);

        await _secretManager.DidNotReceive().RotateSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithNewSecret_RotatesIt()
    {
        _secretManager.GetSecretHistoryAsync("TestSecret", Arg.Any<CancellationToken>())
            .Returns(Enumerable.Empty<SecretVersion>());

        // Wait for the rotation itself rather than for a fixed delay: a background
        // service that is merely slow to be scheduled must not fail the test.
        var rotated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _secretManager.RotateSecretAsync("TestSecret", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                rotated.TrySetResult();
                return "new-value";
            });

        var service = CreateService();

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var finished = await Task.WhenAny(rotated.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        await cts.CancelAsync();
        await service.StopAsync(CancellationToken.None);

        finished.Should().BeSameAs(rotated.Task, "the secret should have been rotated");
    }

    [Fact]
    public async Task ExecuteAsync_WithRecentSecret_SkipsRotation()
    {
        var recentVersion = new SecretVersion
        {
            Name = "TestSecret",
            Version = 1,
            CreatedAt = DateTime.UtcNow, // Just created
            IsActive = true
        };
        _secretManager.GetSecretHistoryAsync("TestSecret", Arg.Any<CancellationToken>())
            .Returns(new[] { recentVersion });

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(150);
        await service.StopAsync(CancellationToken.None);

        await _secretManager.DidNotReceive().RotateSecretAsync("TestSecret", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_RotationThrows_ContinuesProcessing()
    {
        _secretManager.GetSecretHistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("DB error"));

        var service = CreateService();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () =>
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(150);
            await service.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }
}
