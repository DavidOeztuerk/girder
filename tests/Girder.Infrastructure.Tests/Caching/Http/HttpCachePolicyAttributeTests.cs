using Infrastructure.Caching.Http;

namespace Infrastructure.Tests.Caching.Http;

[Trait("Category", "Unit")]
public class HttpCachePolicyAttributeTests
{
    #region BuildCacheControlHeader

    [Fact]
    public void BuildCacheControlHeader_NoStore_ReturnsNoStore()
    {
        var attr = new HttpCachePolicyAttribute { NoStore = true };

        attr.BuildCacheControlHeader().Should().Be("no-store");
    }

    [Fact]
    public void BuildCacheControlHeader_Private_ReturnsPrivate()
    {
        var attr = new HttpCachePolicyAttribute { Private = true, MaxAge = 300 };

        var result = attr.BuildCacheControlHeader();

        result.Should().Contain("private");
        result.Should().Contain("max-age=300");
    }

    [Fact]
    public void BuildCacheControlHeader_Public_ReturnsPublic()
    {
        var attr = new HttpCachePolicyAttribute { Private = false, MaxAge = 3600 };

        var result = attr.BuildCacheControlHeader();

        result.Should().Contain("public");
        result.Should().Contain("max-age=3600");
    }

    [Fact]
    public void BuildCacheControlHeader_NoCache_IncludesNoCache()
    {
        var attr = new HttpCachePolicyAttribute { NoCache = true, Private = false };

        var result = attr.BuildCacheControlHeader();

        result.Should().Contain("no-cache");
    }

    [Fact]
    public void BuildCacheControlHeader_MustRevalidate_IncludesMustRevalidate()
    {
        var attr = new HttpCachePolicyAttribute { MustRevalidate = true, Private = true };

        var result = attr.BuildCacheControlHeader();

        result.Should().Contain("must-revalidate");
    }

    [Fact]
    public void BuildCacheControlHeader_ZeroMaxAge_DoesNotIncludeMaxAge()
    {
        var attr = new HttpCachePolicyAttribute { Private = true, MaxAge = 0 };

        var result = attr.BuildCacheControlHeader();

        result.Should().NotContain("max-age");
    }

    #endregion

    #region GetVaryHeaders

    [Fact]
    public void GetVaryHeaders_NoVary_ReturnsEmpty()
    {
        var attr = new HttpCachePolicyAttribute();

        attr.GetVaryHeaders().Should().BeEmpty();
    }

    [Fact]
    public void GetVaryHeaders_VaryByUser_IncludesAuthorization()
    {
        var attr = new HttpCachePolicyAttribute { VaryByUser = true };

        attr.GetVaryHeaders().Should().Contain("Authorization");
    }

    [Fact]
    public void GetVaryHeaders_VaryByHeaders_IncludesHeaders()
    {
        var attr = new HttpCachePolicyAttribute { VaryByHeaders = "Accept-Language, X-Custom" };

        var headers = attr.GetVaryHeaders();
        headers.Should().Contain("Accept-Language");
        headers.Should().Contain("X-Custom");
    }

    [Fact]
    public void GetVaryHeaders_VaryByUserAndHeaders_IncludesAll()
    {
        var attr = new HttpCachePolicyAttribute { VaryByUser = true, VaryByHeaders = "Accept" };

        var headers = attr.GetVaryHeaders();
        headers.Should().Contain("Authorization");
        headers.Should().Contain("Accept");
    }

    [Fact]
    public void GetVaryHeaders_EmptyVaryByHeaders_ReturnsEmpty()
    {
        var attr = new HttpCachePolicyAttribute { VaryByHeaders = "" };

        attr.GetVaryHeaders().Should().BeEmpty();
    }

    [Fact]
    public void GetVaryHeaders_WhitespaceVaryByHeaders_ReturnsEmpty()
    {
        var attr = new HttpCachePolicyAttribute { VaryByHeaders = "   " };

        attr.GetVaryHeaders().Should().BeEmpty();
    }

    #endregion

    #region Default Values

    [Fact]
    public void Defaults_PrivateIsTrue()
    {
        var attr = new HttpCachePolicyAttribute();

        attr.Private.Should().BeTrue();
        attr.NoStore.Should().BeFalse();
        attr.NoCache.Should().BeFalse();
        attr.MustRevalidate.Should().BeFalse();
        attr.MaxAge.Should().Be(0);
        attr.VaryByUser.Should().BeFalse();
        attr.VaryByHeaders.Should().BeNull();
    }

    #endregion
}
