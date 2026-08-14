using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class TokenCleanupServiceTests
{
    private readonly ITokenRevocationService _tokenRevocationService;
    private readonly ILogger<TokenCleanupService> _logger;

    public TokenCleanupServiceTests()
    {
        _tokenRevocationService = Substitute.For<ITokenRevocationService>();
        _logger = Substitute.For<ILogger<TokenCleanupService>>();
    }

    [Fact]
    public async Task ExecuteAsync_CallsCleanupExpiredTokens()
    {
        var service = new TokenCleanupService(_tokenRevocationService, _logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        await service.StopAsync(CancellationToken.None);

        await _tokenRevocationService.Received().CleanupExpiredTokensAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CleanupThrows_ContinuesProcessing()
    {
        _tokenRevocationService.CleanupExpiredTokensAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Cleanup failed"));

        var service = new TokenCleanupService(_tokenRevocationService, _logger);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () =>
        {
            await service.StartAsync(cts.Token);
            await Task.Delay(100);
            await service.StopAsync(CancellationToken.None);
        };

        await act.Should().NotThrowAsync();
    }
}
