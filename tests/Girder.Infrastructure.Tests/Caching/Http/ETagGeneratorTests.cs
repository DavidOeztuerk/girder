using Girder.Application.Abstractions;
using Girder.Application.Interfaces;
using Girder.Infrastructure.Caching;
using Girder.Infrastructure.Caching.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Caching.Http;

[Trait("Category", "Unit")]
public class ETagGeneratorTests
{
    private readonly IDistributedCacheService _cacheService;
    private readonly ILogger<ETagGenerator> _logger;
    private readonly ETagGenerator _sut;
    private readonly ETagGenerator _sutWithoutCache;

    public ETagGeneratorTests()
    {
        _cacheService = Substitute.For<IDistributedCacheService>();
        _logger = Substitute.For<ILogger<ETagGenerator>>();
        _sut = new ETagGenerator(_cacheService, _logger);
        _sutWithoutCache = new ETagGenerator(null, _logger);
    }

    #region GenerateETag(byte[])

    [Fact]
    public void GenerateETag_NullBytes_ReturnsZeroETag()
    {
        var etag = _sut.GenerateETag((byte[])null!);

        etag.Should().Be("\"0\"");
    }

    [Fact]
    public void GenerateETag_EmptyBytes_ReturnsZeroETag()
    {
        var etag = _sut.GenerateETag(Array.Empty<byte>());

        etag.Should().Be("\"0\"");
    }

    [Fact]
    public void GenerateETag_ValidBytes_ReturnsQuotedHash()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("test content");

        var etag = _sut.GenerateETag(content);

        etag.Should().StartWith("\"");
        etag.Should().EndWith("\"");
        etag.Length.Should().BeGreaterThan(2);
    }

    [Fact]
    public void GenerateETag_SameBytes_ReturnsSameETag()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("test content");

        var etag1 = _sut.GenerateETag(content);
        var etag2 = _sut.GenerateETag(content);

        etag1.Should().Be(etag2);
    }

    [Fact]
    public void GenerateETag_DifferentBytes_ReturnsDifferentETags()
    {
        var content1 = System.Text.Encoding.UTF8.GetBytes("content A");
        var content2 = System.Text.Encoding.UTF8.GetBytes("content B");

        var etag1 = _sut.GenerateETag(content1);
        var etag2 = _sut.GenerateETag(content2);

        etag1.Should().NotBe(etag2);
    }

    #endregion

    #region GenerateETag(string)

    [Fact]
    public void GenerateETag_NullString_ReturnsZeroETag()
    {
        var etag = _sut.GenerateETag((string)null!);

        etag.Should().Be("\"0\"");
    }

    [Fact]
    public void GenerateETag_EmptyString_ReturnsZeroETag()
    {
        var etag = _sut.GenerateETag(string.Empty);

        etag.Should().Be("\"0\"");
    }

    [Fact]
    public void GenerateETag_ValidString_ReturnsQuotedHash()
    {
        var etag = _sut.GenerateETag("hello world");

        etag.Should().StartWith("\"");
        etag.Should().EndWith("\"");
    }

    #endregion

    #region GenerateETag(Guid, DateTime)

    [Fact]
    public void GenerateETag_GuidAndDateTime_ReturnsQuotedHash()
    {
        var id = Guid.NewGuid();
        var updated = DateTime.UtcNow;

        var etag = _sut.GenerateETag(id, updated);

        etag.Should().StartWith("\"");
        etag.Should().EndWith("\"");
    }

    [Fact]
    public void GenerateETag_SameGuidAndDateTime_ReturnsSameETag()
    {
        var id = Guid.NewGuid();
        var updated = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        var etag1 = _sut.GenerateETag(id, updated);
        var etag2 = _sut.GenerateETag(id, updated);

        etag1.Should().Be(etag2);
    }

    #endregion

    #region GenerateETag(Guid, DateTime, int?)

    [Fact]
    public void GenerateETag_WithVersion_ReturnsDifferentETagFromWithout()
    {
        var id = Guid.NewGuid();
        var updated = DateTime.UtcNow;

        var etagNoVersion = _sut.GenerateETag(id, updated, null);
        var etagWithVersion = _sut.GenerateETag(id, updated, 1);

        etagNoVersion.Should().NotBe(etagWithVersion);
    }

    [Fact]
    public void GenerateETag_DifferentVersions_ReturnDifferentETags()
    {
        var id = Guid.NewGuid();
        var updated = DateTime.UtcNow;

        var etag1 = _sut.GenerateETag(id, updated, 1);
        var etag2 = _sut.GenerateETag(id, updated, 2);

        etag1.Should().NotBe(etag2);
    }

    #endregion

    #region ValidateETag

    [Fact]
    public void ValidateETag_NullProvided_ReturnsFalse()
    {
        _sut.ValidateETag(null, "\"abc\"").Should().BeFalse();
    }

    [Fact]
    public void ValidateETag_EmptyProvided_ReturnsFalse()
    {
        _sut.ValidateETag("", "\"abc\"").Should().BeFalse();
    }

    [Fact]
    public void ValidateETag_WhitespaceProvided_ReturnsFalse()
    {
        _sut.ValidateETag("   ", "\"abc\"").Should().BeFalse();
    }

    [Fact]
    public void ValidateETag_MatchingETags_ReturnsTrue()
    {
        _sut.ValidateETag("\"abc123\"", "\"abc123\"").Should().BeTrue();
    }

    [Fact]
    public void ValidateETag_NonMatchingETags_ReturnsFalse()
    {
        _sut.ValidateETag("\"abc\"", "\"xyz\"").Should().BeFalse();
    }

    [Fact]
    public void ValidateETag_Wildcard_ReturnsTrue()
    {
        _sut.ValidateETag("*", "\"any-etag\"").Should().BeTrue();
    }

    [Fact]
    public void ValidateETag_WeakETag_StripsPrefix()
    {
        _sut.ValidateETag("W/\"abc123\"", "\"abc123\"").Should().BeTrue();
    }

    [Fact]
    public void ValidateETag_MultipleETags_MatchesAny()
    {
        _sut.ValidateETag("\"aaa\", \"bbb\", \"ccc\"", "\"bbb\"").Should().BeTrue();
    }

    [Fact]
    public void ValidateETag_MultipleETags_NoneMatch_ReturnsFalse()
    {
        _sut.ValidateETag("\"aaa\", \"bbb\"", "\"ccc\"").Should().BeFalse();
    }

    #endregion

    #region StoreETagAsync

    [Fact]
    public async Task StoreETagAsync_NoCacheService_DoesNotThrow()
    {
        var act = () => _sutWithoutCache.StoreETagAsync("key", "\"etag\"");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StoreETagAsync_WithCacheService_StoresWithPrefix()
    {
        await _sut.StoreETagAsync("key1", "\"etag1\"");

        await _cacheService.Received(1).SetAsync(
            "etag:key1", "\"etag1\"", Arg.Any<TimeSpan>(), Arg.Any<CacheOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreETagAsync_WithCustomExpiry_UsesCustomExpiry()
    {
        await _sut.StoreETagAsync("key1", "\"etag1\"", TimeSpan.FromMinutes(10));

        await _cacheService.Received(1).SetAsync(
            "etag:key1", "\"etag1\"", TimeSpan.FromMinutes(10), Arg.Any<CacheOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StoreETagAsync_CacheThrows_DoesNotRethrow()
    {
        _cacheService.SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>(), Arg.Any<CacheOptions?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("cache error"));

        var act = () => _sut.StoreETagAsync("key", "\"etag\"");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetCachedETagAsync

    [Fact]
    public async Task GetCachedETagAsync_NoCacheService_ReturnsNull()
    {
        var result = await _sutWithoutCache.GetCachedETagAsync("key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCachedETagAsync_CacheHit_ReturnsETag()
    {
        _cacheService.GetAsync<string>("etag:key1", Arg.Any<CancellationToken>())
            .Returns("\"cached-etag\"");

        var result = await _sut.GetCachedETagAsync("key1");

        result.Should().Be("\"cached-etag\"");
    }

    [Fact]
    public async Task GetCachedETagAsync_CacheThrows_ReturnsNull()
    {
        _cacheService.GetAsync<string>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("cache error"));

        var result = await _sut.GetCachedETagAsync("key1");

        result.Should().BeNull();
    }

    #endregion

    #region InvalidateETagAsync

    [Fact]
    public async Task InvalidateETagAsync_NoCacheService_DoesNotThrow()
    {
        var act = () => _sutWithoutCache.InvalidateETagAsync("key");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateETagAsync_WithCacheService_RemovesKey()
    {
        await _sut.InvalidateETagAsync("key1");

        await _cacheService.Received(1).RemoveAsync("etag:key1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateETagAsync_CacheThrows_DoesNotRethrow()
    {
        _cacheService.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("cache error"));

        var act = () => _sut.InvalidateETagAsync("key");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region InvalidateETagsByPatternAsync

    [Fact]
    public async Task InvalidateETagsByPatternAsync_NoCacheService_DoesNotThrow()
    {
        var act = () => _sutWithoutCache.InvalidateETagsByPatternAsync("/api/skills*");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InvalidateETagsByPatternAsync_WithCacheService_CallsRemoveByPattern()
    {
        await _sut.InvalidateETagsByPatternAsync("/api/skills*");

        await _cacheService.Received(1).RemoveByPatternAsync("etag:/api/skills*", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateETagsByPatternAsync_CacheThrows_DoesNotRethrow()
    {
        _cacheService.RemoveByPatternAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("cache error"));

        var act = () => _sut.InvalidateETagsByPatternAsync("/api/skills*");

        await act.Should().NotThrowAsync();
    }

    #endregion
}
