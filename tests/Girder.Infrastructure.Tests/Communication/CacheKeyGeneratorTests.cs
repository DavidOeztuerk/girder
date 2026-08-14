using Girder.Infrastructure.Communication.Caching;

namespace Girder.Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class CacheKeyGeneratorTests
{
    #region GenerateKey (GET) — Basic Format

    [Fact]
    public void GenerateKey_BasicInput_ReturnsPrefixedKey()
    {
        var key = CacheKeyGenerator.GenerateKey("UserService", "/api/users");

        key.Should().Be("svc:UserService:/api/users");
    }

    [Fact]
    public void GenerateKey_SameInputTwice_ReturnsSameKey()
    {
        var key1 = CacheKeyGenerator.GenerateKey("UserService", "/api/users/123");
        var key2 = CacheKeyGenerator.GenerateKey("UserService", "/api/users/123");

        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateKey_DifferentServices_ReturnsDifferentKeys()
    {
        var key1 = CacheKeyGenerator.GenerateKey("UserService", "/api/users");
        var key2 = CacheKeyGenerator.GenerateKey("SkillService", "/api/users");

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void GenerateKey_DifferentEndpoints_ReturnsDifferentKeys()
    {
        var key1 = CacheKeyGenerator.GenerateKey("UserService", "/api/users");
        var key2 = CacheKeyGenerator.GenerateKey("UserService", "/api/skills");

        key1.Should().NotBe(key2);
    }

    #endregion

    #region GenerateKey (GET) — Headers

    [Fact]
    public void GenerateKey_NullHeaders_OmitsHeadersPart()
    {
        var key = CacheKeyGenerator.GenerateKey("Svc", "/api/test", null);

        key.Should().Be("svc:Svc:/api/test");
    }

    [Fact]
    public void GenerateKey_EmptyHeaders_OmitsHeadersPart()
    {
        var key = CacheKeyGenerator.GenerateKey("Svc", "/api/test", new Dictionary<string, string>());

        key.Should().Be("svc:Svc:/api/test");
    }

    [Fact]
    public void GenerateKey_CacheableHeader_IncludedInKey()
    {
        var headers = new Dictionary<string, string> { ["X-Custom"] = "value1" };

        var keyWithHeaders = CacheKeyGenerator.GenerateKey("Svc", "/api/test", headers);
        var keyWithout = CacheKeyGenerator.GenerateKey("Svc", "/api/test");

        keyWithHeaders.Should().NotBe(keyWithout);
        keyWithHeaders.Should().StartWith("svc:Svc:/api/test:");
    }

    [Fact]
    public void GenerateKey_NonCacheableHeaders_Excluded()
    {
        // All of these should be excluded
        var nonCacheable = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer token",
            ["Cookie"] = "session=abc",
            ["Set-Cookie"] = "x=y",
            ["X-Request-ID"] = "req-123",
            ["User-Agent"] = "TestAgent",
            ["Date"] = "Mon, 01 Jan 2024",
            ["Age"] = "300",
            ["Expires"] = "Thu, 01 Dec 2025"
        };

        var keyWithNonCacheable = CacheKeyGenerator.GenerateKey("Svc", "/api/test", nonCacheable);
        var keyWithout = CacheKeyGenerator.GenerateKey("Svc", "/api/test");

        keyWithNonCacheable.Should().Be(keyWithout, "all non-cacheable headers should be filtered out");
    }

    [Fact]
    public void GenerateKey_NonCacheableHeaders_CaseInsensitive()
    {
        var headers = new Dictionary<string, string> { ["authorization"] = "Bearer token" };

        var key = CacheKeyGenerator.GenerateKey("Svc", "/api/test", headers);
        var keyWithout = CacheKeyGenerator.GenerateKey("Svc", "/api/test");

        key.Should().Be(keyWithout, "header filtering should be case-insensitive");
    }

    [Fact]
    public void GenerateKey_MixedHeaders_OnlyCacheableIncluded()
    {
        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer token",
            ["Accept-Language"] = "en-US"
        };

        var keyMixed = CacheKeyGenerator.GenerateKey("Svc", "/api/test", headers);

        // Only Accept-Language should be included
        var headersOnlyCacheable = new Dictionary<string, string>
        {
            ["Accept-Language"] = "en-US"
        };
        var keyOnlyCacheable = CacheKeyGenerator.GenerateKey("Svc", "/api/test", headersOnlyCacheable);

        keyMixed.Should().Be(keyOnlyCacheable);
    }

    [Fact]
    public void GenerateKey_HeaderOrder_DoesNotAffectKey()
    {
        var headers1 = new Dictionary<string, string>
        {
            ["Accept-Language"] = "en",
            ["X-Custom"] = "val"
        };
        var headers2 = new Dictionary<string, string>
        {
            ["X-Custom"] = "val",
            ["Accept-Language"] = "en"
        };

        var key1 = CacheKeyGenerator.GenerateKey("Svc", "/api/test", headers1);
        var key2 = CacheKeyGenerator.GenerateKey("Svc", "/api/test", headers2);

        key1.Should().Be(key2, "headers are sorted before key generation");
    }

    #endregion

    #region GenerateKey (GET) — Long Key Hashing

    [Fact]
    public void GenerateKey_ShortKey_NotHashed()
    {
        var key = CacheKeyGenerator.GenerateKey("Svc", "/api/short");

        key.Should().NotContain("=");
        key.Should().StartWith("svc:Svc:/api/short");
    }

    [Fact]
    public void GenerateKey_LongKey_HashedToShorterKey()
    {
        var longEndpoint = "/api/" + new string('x', 300);

        var key = CacheKeyGenerator.GenerateKey("Svc", longEndpoint);

        key.Should().StartWith("svc:Svc:");
        key.Length.Should().BeLessThan(300, "long keys are hashed to shorter form");
    }

    [Fact]
    public void GenerateKey_LongKey_Deterministic()
    {
        var longEndpoint = "/api/" + new string('a', 300);

        var key1 = CacheKeyGenerator.GenerateKey("Svc", longEndpoint);
        var key2 = CacheKeyGenerator.GenerateKey("Svc", longEndpoint);

        key1.Should().Be(key2);
    }

    #endregion

    #region GenerateKey<T> (POST) — Request Body

    [Fact]
    public void GenerateKeyPost_IncludesRequestBodyHash()
    {
        var request = new TestRequest { Name = "test", Value = 42 };

        var postKey = CacheKeyGenerator.GenerateKey("Svc", "/api/test", request);
        var getKey = CacheKeyGenerator.GenerateKey("Svc", "/api/test");

        postKey.Should().NotBe(getKey, "POST key includes request body hash");
        postKey.Should().StartWith("svc:Svc:/api/test:");
    }

    [Fact]
    public void GenerateKeyPost_SameRequest_SameKey()
    {
        var req1 = new TestRequest { Name = "test", Value = 42 };
        var req2 = new TestRequest { Name = "test", Value = 42 };

        var key1 = CacheKeyGenerator.GenerateKey("Svc", "/api/test", req1);
        var key2 = CacheKeyGenerator.GenerateKey("Svc", "/api/test", req2);

        key1.Should().Be(key2);
    }

    [Fact]
    public void GenerateKeyPost_DifferentRequest_DifferentKey()
    {
        var req1 = new TestRequest { Name = "alice", Value = 1 };
        var req2 = new TestRequest { Name = "bob", Value = 2 };

        var key1 = CacheKeyGenerator.GenerateKey("Svc", "/api/test", req1);
        var key2 = CacheKeyGenerator.GenerateKey("Svc", "/api/test", req2);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void GenerateKeyPost_WithCacheableHeaders_IncludesBoth()
    {
        var request = new TestRequest { Name = "test" };
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };

        var keyWithHeaders = CacheKeyGenerator.GenerateKey("Svc", "/api/test", request, headers);
        var keyWithout = CacheKeyGenerator.GenerateKey("Svc", "/api/test", request);

        keyWithHeaders.Should().NotBe(keyWithout);
    }

    [Fact]
    public void GenerateKeyPost_NonCacheableHeaders_Excluded()
    {
        var request = new TestRequest { Name = "test" };
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer token" };

        var keyWithAuth = CacheKeyGenerator.GenerateKey("Svc", "/api/test", request, headers);
        var keyWithout = CacheKeyGenerator.GenerateKey("Svc", "/api/test", request);

        keyWithAuth.Should().Be(keyWithout);
    }

    #endregion

    #region Hash Output — URL-Safe Base64

    [Fact]
    public void GenerateKeyPost_HashOutput_IsUrlSafeBase64()
    {
        var request = new TestRequest { Name = "test-for-hash-format" };

        var key = CacheKeyGenerator.GenerateKey("Svc", "/api/test", request);

        // Extract the hash portion (after "svc:Svc:/api/test:")
        var hashPart = key.Substring("svc:Svc:/api/test:".Length);
        hashPart.Should().NotContain("+");
        hashPart.Should().NotContain("/");
        hashPart.Should().NotEndWith("=");
    }

    #endregion

    private class TestRequest
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }
}
