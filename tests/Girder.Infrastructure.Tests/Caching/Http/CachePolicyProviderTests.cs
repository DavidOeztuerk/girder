using Girder.Infrastructure.Caching.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Caching.Http;

[Trait("Category", "Unit")]
public class CachePolicyProviderTests
{
    private readonly ILogger<CachePolicyProvider> _logger;

    public CachePolicyProviderTests()
    {
        _logger = Substitute.For<ILogger<CachePolicyProvider>>();
    }

    private CachePolicyProvider CreateSut(HttpCachingOptions? options = null)
    {
        var opts = Options.Create(options ?? new HttpCachingOptions());
        return new CachePolicyProvider(opts, _logger);
    }

    #region Disabled Globally

    [Fact]
    public void GetCachePolicy_Disabled_ReturnsNull()
    {
        var sut = CreateSut(new HttpCachingOptions { Enabled = false });

        var result = sut.GetCachePolicy("/api/test", "GET");

        result.Should().BeNull();
    }

    #endregion

    #region Non-Cacheable Methods

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("OPTIONS")]
    public void GetCachePolicy_NonCacheableMethod_ReturnsNoStore(string method)
    {
        var sut = CreateSut();

        var result = sut.GetCachePolicy("/api/test", method);

        result.Should().NotBeNull();
        result!.NoStore.Should().BeTrue();
        result.CacheControl.Should().Be("no-store");
        result.PolicySource.Should().Be("non-cacheable-method");
    }

    #endregion

    #region Non-Cacheable Paths

    [Theory]
    [InlineData("/hub/chat")]
    [InlineData("/hubs/notification")]
    [InlineData("/api/auth/login")]
    [InlineData("/health")]
    [InlineData("/swagger")]
    [InlineData("/hangfire")]
    public void GetCachePolicy_NonCacheablePath_ReturnsNoStore(string path)
    {
        var sut = CreateSut();

        var result = sut.GetCachePolicy(path, "GET");

        result.Should().NotBeNull();
        result!.NoStore.Should().BeTrue();
        result.PolicySource.Should().Be("non-cacheable-path");
    }

    [Fact]
    public void GetCachePolicy_ApplicationDeclaredPath_ReturnsNoStore()
    {
        var sut = CreateSut(new HttpCachingOptions
        {
            AdditionalNonCacheablePaths = ["/api/live/"]
        });

        var result = sut.GetCachePolicy("/api/live/room-1", "GET");

        result.Should().NotBeNull();
        result!.NoStore.Should().BeTrue();
        result.PolicySource.Should().Be("non-cacheable-path");
    }

    [Fact]
    public void GetCachePolicy_UndeclaredApplicationPath_IsNotExcluded()
    {
        // The library knows no application route, so nothing is excluded that
        // the application did not name.
        var sut = CreateSut();

        var result = sut.GetCachePolicy("/api/live/room-1", "GET");

        result!.PolicySource.Should().NotBe("non-cacheable-path");
    }

    #endregion

    #region Default Policy — User-Specific Paths

    [Theory]
    [InlineData("/api/profile")]
    [InlineData("/api/user/settings")]
    [InlineData("/api/my-jobs")]
    [InlineData("/api/my/bookings")]
    [InlineData("/api/jobs")]
    [InlineData("/api/bookings")]
    [InlineData("/api/referrals")]
    [InlineData("/api/notifications")]
    public void GetCachePolicy_UserSpecificPath_ReturnsNoStore(string path)
    {
        var sut = CreateSut();

        var result = sut.GetCachePolicy(path, "GET");

        result.Should().NotBeNull();
        result!.NoStore.Should().BeTrue();
        result.IsPrivate.Should().BeTrue();
        result.GenerateETag.Should().BeFalse();
        result.PolicySource.Should().Be("default-private");
    }

    #endregion

    #region Default Policy — Public Paths

    [Fact]
    public void GetCachePolicy_PublicPath_ReturnsPublicCachePolicy()
    {
        var sut = CreateSut(new HttpCachingOptions { DefaultPublicMaxAge = 3600, ETagEnabled = true });

        var result = sut.GetCachePolicy("/api/categories", "GET");

        result.Should().NotBeNull();
        result!.IsPrivate.Should().BeFalse();
        result.MaxAge.Should().Be(3600);
        result.GenerateETag.Should().BeTrue();
        result.PolicySource.Should().Be("default-public");
    }

    #endregion

    #region Configured Policies

    [Fact]
    public void GetCachePolicy_MatchingConfiguredPolicy_ReturnsPolicyFromConfig()
    {
        var options = new HttpCachingOptions
        {
            Policies = new List<HttpCachePolicy>
            {
                new HttpCachePolicy
                {
                    PathPattern = "/api/categories/**",
                    MaxAge = 600,
                    Private = false,
                    VaryByHeaders = new List<string>()
                }
            }
        };
        var sut = CreateSut(options);

        var result = sut.GetCachePolicy("/api/categories/123", "GET");

        result.Should().NotBeNull();
        result!.MaxAge.Should().Be(600);
        result.PolicySource.Should().Contain("config");
    }

    [Fact]
    public void GetCachePolicy_NoStoreConfiguredPolicy_DisablesETag()
    {
        var options = new HttpCachingOptions
        {
            ETagEnabled = true,
            Policies = new List<HttpCachePolicy>
            {
                new HttpCachePolicy
                {
                    PathPattern = "/api/status",
                    NoStore = true,
                    VaryByHeaders = new List<string>()
                }
            }
        };
        var sut = CreateSut(options);

        var result = sut.GetCachePolicy("/api/status", "GET");

        result.Should().NotBeNull();
        result!.NoStore.Should().BeTrue();
        result.GenerateETag.Should().BeFalse();
    }

    [Fact]
    public void GetCachePolicy_PolicyWithVaryByUser_IncludesAuthorizationVary()
    {
        var options = new HttpCachingOptions
        {
            Policies = new List<HttpCachePolicy>
            {
                new HttpCachePolicy
                {
                    PathPattern = "/api/dashboard",
                    MaxAge = 60,
                    VaryByUser = true,
                    VaryByHeaders = new List<string>()
                }
            }
        };
        var sut = CreateSut(options);

        var result = sut.GetCachePolicy("/api/dashboard", "GET");

        result.Should().NotBeNull();
        result!.VaryHeaders.Should().Contain("Authorization");
    }

    [Fact]
    public void GetCachePolicy_PolicyWithVaryByHeaders_IncludesHeaders()
    {
        var options = new HttpCachingOptions
        {
            Policies = new List<HttpCachePolicy>
            {
                new HttpCachePolicy
                {
                    PathPattern = "/api/data",
                    MaxAge = 60,
                    VaryByHeaders = new List<string> { "Accept-Language", "X-Custom" }
                }
            }
        };
        var sut = CreateSut(options);

        var result = sut.GetCachePolicy("/api/data", "GET");

        result.Should().NotBeNull();
        result!.VaryHeaders.Should().Contain("Accept-Language");
        result.VaryHeaders.Should().Contain("X-Custom");
    }

    #endregion

    #region BuildCacheControlHeader via Policy Results

    [Fact]
    public void GetCachePolicy_PublicNoCachePolicy_IncludesNoCacheDirective()
    {
        var options = new HttpCachingOptions
        {
            Policies = new List<HttpCachePolicy>
            {
                new HttpCachePolicy
                {
                    PathPattern = "/api/revalidate",
                    NoCache = true,
                    Private = false,
                    MaxAge = 300,
                    VaryByHeaders = new List<string>()
                }
            }
        };
        var sut = CreateSut(options);

        var result = sut.GetCachePolicy("/api/revalidate", "GET");

        result.Should().NotBeNull();
        result!.CacheControl.Should().Contain("no-cache");
        result.CacheControl.Should().Contain("must-revalidate");
        result.CacheControl.Should().Contain("public");
    }

    #endregion

    #region Glob Pattern Matching

    [Theory]
    [InlineData("/api/users/*/profile", "/api/users/123/profile", true)]
    [InlineData("/api/users/**", "/api/users/123/profile/photo", true)]
    [InlineData("/api/users/*", "/api/users/123/profile", false)]
    public void GetCachePolicy_GlobPatterns_MatchCorrectly(string pattern, string path, bool shouldMatch)
    {
        var options = new HttpCachingOptions
        {
            Policies = new List<HttpCachePolicy>
            {
                new HttpCachePolicy
                {
                    PathPattern = pattern,
                    MaxAge = 100,
                    Private = false,
                    VaryByHeaders = new List<string>()
                }
            }
        };
        var sut = CreateSut(options);

        var result = sut.GetCachePolicy(path, "GET");

        if (shouldMatch)
        {
            result!.PolicySource.Should().Contain("config");
        }
        else
        {
            result!.PolicySource.Should().NotContain("config");
        }
    }

    #endregion
}
