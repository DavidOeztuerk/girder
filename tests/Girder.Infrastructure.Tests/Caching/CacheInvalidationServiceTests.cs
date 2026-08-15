using Girder.Application.Abstractions;
using Girder.Application.Interfaces;
using Girder.Infrastructure.Caching;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Caching;

[Trait("Category", "Unit")]
public class CacheInvalidationServiceTests
{
    private readonly IDistributedCacheService _cacheService;
    private readonly ILogger<CacheInvalidationService> _logger;
    private readonly CacheInvalidationService _sut;

    public CacheInvalidationServiceTests()
    {
        _cacheService = Substitute.For<IDistributedCacheService>();
        _logger = Substitute.For<ILogger<CacheInvalidationService>>();
        _sut = new CacheInvalidationService(_cacheService, _logger);
    }

    #region Constructor

    [Fact]
    public void Constructor_RegistersNoRules()
    {
        var stats = _sut.GetStatistics();

        stats.TotalRules.Should().Be(0);
        stats.RegisteredEventTypes.Should().BeEmpty();
    }

    #endregion

    #region RegisterInvalidationRule

    [Fact]
    public void RegisterInvalidationRule_AddsRule()
    {
        var statsBefore = _sut.GetStatistics();
        var countBefore = statsBefore.TotalRules;

        _sut.RegisterInvalidationRule<CustomTestEvent>("custom:*", "custom-tag");

        var statsAfter = _sut.GetStatistics();
        statsAfter.TotalRules.Should().Be(countBefore + 1);
    }

    #endregion

    #region RegisterTagInvalidationRule

    [Fact]
    public void RegisterTagInvalidationRule_AddsTagRule()
    {
        var statsBefore = _sut.GetStatistics();
        var countBefore = statsBefore.TotalRules;

        _sut.RegisterTagInvalidationRule<CustomTestEvent>("tag-a", "tag-b");

        var statsAfter = _sut.GetStatistics();
        statsAfter.TotalRules.Should().Be(countBefore + 1);
    }

    #endregion

    #region InvalidateAsync

    [Fact]
    public async Task InvalidateAsync_NoRulesForEvent_DoesNothing()
    {
        // CustomTestEvent has no registered rules
        var evt = new CustomTestEvent("123");

        await _sut.InvalidateAsync(evt);

        await _cacheService.DidNotReceive().RemoveByPatternAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cacheService.DidNotReceive().RemoveByTagsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_RegisteredPattern_CallsRemoveByPattern()
    {
        _sut.RegisterInvalidationRule<CustomTestEvent>("item:*");
        var evt = new CustomTestEvent("123");

        await _sut.InvalidateAsync(evt);

        await _cacheService.Received().RemoveByPatternAsync("item:*", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_PatternWithToken_SubstitutesEventProperty()
    {
        _sut.RegisterInvalidationRule<CustomTestEvent>("item:{TestId}:*");
        var evt = new CustomTestEvent("abc-456");

        await _sut.InvalidateAsync(evt);

        await _cacheService.Received().RemoveByPatternAsync("item:abc-456:*", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateAsync_TagRule_CallsRemoveByTags()
    {
        _sut.RegisterTagInvalidationRule<CustomTestEvent>("custom-tag");
        var evt = new CustomTestEvent("789");

        await _sut.InvalidateAsync(evt);

        await _cacheService.Received().RemoveByTagsAsync(
            Arg.Is<IEnumerable<string>>(t => t.Contains("custom-tag")),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region InvalidateCacheAsync

    [Fact]
    public async Task InvalidateCacheAsync_WithKeys_RemovesKeys()
    {
        var request = new CacheInvalidationRequest
        {
            Keys = new List<string> { "key1", "key2" }
        };

        await _sut.InvalidateCacheAsync(request);

        await _cacheService.Received(1).RemoveAsync(
            Arg.Is<IEnumerable<string>>(k => k.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateCacheAsync_WithPatterns_RemovesByPattern()
    {
        var request = new CacheInvalidationRequest
        {
            Patterns = new List<string> { "user:*", "job:*" }
        };

        await _sut.InvalidateCacheAsync(request);

        await _cacheService.Received(1).RemoveByPatternAsync("user:*", Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveByPatternAsync("job:*", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateCacheAsync_WithTags_RemovesByTags()
    {
        var request = new CacheInvalidationRequest
        {
            Tags = new List<string> { "tag1", "tag2" }
        };

        await _sut.InvalidateCacheAsync(request);

        await _cacheService.Received(1).RemoveByTagsAsync(
            Arg.Is<IEnumerable<string>>(t => t.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateCacheAsync_EmptyRequest_DoesNothing()
    {
        var request = new CacheInvalidationRequest();

        await _sut.InvalidateCacheAsync(request);

        await _cacheService.DidNotReceive().RemoveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        await _cacheService.DidNotReceive().RemoveByPatternAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _cacheService.DidNotReceive().RemoveByTagsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateCacheAsync_AllOptions_ExecutesAll()
    {
        var request = new CacheInvalidationRequest
        {
            Keys = new List<string> { "key1" },
            Patterns = new List<string> { "pattern:*" },
            Tags = new List<string> { "tag1" }
        };

        await _sut.InvalidateCacheAsync(request);

        await _cacheService.Received(1).RemoveAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveByPatternAsync("pattern:*", Arg.Any<CancellationToken>());
        await _cacheService.Received(1).RemoveByTagsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetStatistics

    [Fact]
    public void GetStatistics_ReturnsValidStatistics()
    {
        _sut.RegisterInvalidationRule<CustomTestEvent>("item:*");

        var stats = _sut.GetStatistics();

        stats.RegisteredEventTypes.Should().NotBeEmpty();
        stats.TotalRules.Should().BeGreaterThan(0);
        stats.RulesByEventType.Should().NotBeEmpty();
        stats.LastUpdated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    // Custom event with no registered rules for negative test
    public record CustomTestEvent(string TestId) : DomainEvent;
}
