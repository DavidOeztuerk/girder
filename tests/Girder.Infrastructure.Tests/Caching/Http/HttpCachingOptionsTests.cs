using Infrastructure.Caching.Http;

namespace Infrastructure.Tests.Caching.Http;

[Trait("Category", "Unit")]
public class HttpCachingOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new HttpCachingOptions();

        options.Enabled.Should().BeTrue();
        options.DefaultPrivateMaxAge.Should().Be(300);
        options.DefaultPublicMaxAge.Should().Be(3600);
        options.ETagEnabled.Should().BeTrue();
        options.AddCacheStatusHeader.Should().BeTrue();
        options.Policies.Should().BeEmpty();
    }

    [Fact]
    public void SectionName_IsHttpCaching()
    {
        HttpCachingOptions.SectionName.Should().Be("HttpCaching");
    }
}

[Trait("Category", "Unit")]
public class HttpCachePolicyTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var policy = new HttpCachePolicy();

        policy.PathPattern.Should().BeEmpty();
        policy.MaxAge.Should().Be(0);
        policy.Private.Should().BeTrue();
        policy.NoStore.Should().BeFalse();
        policy.NoCache.Should().BeFalse();
        policy.MustRevalidate.Should().BeFalse();
        policy.VaryByHeaders.Should().BeEmpty();
        policy.VaryByUser.Should().BeFalse();
    }
}
