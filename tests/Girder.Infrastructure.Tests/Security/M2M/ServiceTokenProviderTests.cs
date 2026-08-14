using Infrastructure.Communication.Configuration;
using Infrastructure.Security.M2M;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Tests.Security.M2M;

[Trait("Category", "Unit")]
public class ServiceTokenProviderTests
{
    private readonly ILogger<ServiceTokenProvider> _logger;
    private readonly IDistributedCache _cache;

    public ServiceTokenProviderTests()
    {
        _logger = Substitute.For<ILogger<ServiceTokenProvider>>();
        _cache = Substitute.For<IDistributedCache>();
    }

    private ServiceTokenProvider CreateProvider(
        HttpClient httpClient,
        M2MConfiguration? config = null,
        IDistributedCache? cache = null)
    {
        var m2mConfig = config ?? new M2MConfiguration
        {
            Enabled = true,
            TokenEndpoint = "http://localhost:8080/api/auth/service-token",
            ClientId = "TestService",
            ClientSecret = "test-secret",
            EnableTokenCaching = false
        };
        var options = Options.Create(new ServiceCommunicationOptions { M2M = m2mConfig });
        return new ServiceTokenProvider(httpClient, _logger, options, cache);
    }

    [Fact]
    public async Task GetTokenAsync_Disabled_ReturnsNull()
    {
        var config = new M2MConfiguration { Enabled = false };
        var provider = CreateProvider(new HttpClient(), config);

        var token = await provider.GetTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenAsync_WithCachedToken_ReturnsCachedValue()
    {
        var tokenInfo = new TokenInfo
        {
            Token = "cached-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        var serialized = JsonSerializer.Serialize(tokenInfo);
        var bytes = Encoding.UTF8.GetBytes(serialized);

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bytes);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = true,
            ClientId = "TestService",
            TokenCacheKeyPrefix = "m2m:token:"
        };
        var provider = CreateProvider(new HttpClient(), config, _cache);

        var token = await provider.GetTokenAsync();

        token.Should().Be("cached-token");
    }

    [Fact]
    public async Task GetTokenAsync_CacheMiss_RequestsNewToken()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new { accessToken = "new-token", expiresIn = 3600 }
        });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.GetTokenAsync();

        token.Should().Be("new-token");
    }

    [Fact]
    public async Task RefreshTokenAsync_Disabled_ReturnsNull()
    {
        var config = new M2MConfiguration { Enabled = false };
        var provider = CreateProvider(new HttpClient(), config);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_FailedRequest_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler("error", HttpStatusCode.InternalServerError);
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_NoDataProperty_ReturnsNull()
    {
        var responseJson = JsonSerializer.Serialize(new { success = true });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_NoAccessTokenProperty_ReturnsNull()
    {
        var responseJson = JsonSerializer.Serialize(new { data = new { noToken = true } });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_EmptyTokenEndpoint_ReturnsNull()
    {
        var config = new M2MConfiguration
        {
            Enabled = true,
            TokenEndpoint = null,
            EnableTokenCaching = false
        };
        var provider = CreateProvider(new HttpClient(), config);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateTokenAsync_RemovesFromCache()
    {
        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = true,
            ClientId = "TestService",
            TokenCacheKeyPrefix = "m2m:token:"
        };
        var provider = CreateProvider(new HttpClient(), config, _cache);

        await provider.InvalidateTokenAsync();

        await _cache.Received(1).RemoveAsync(
            Arg.Is<string>(s => s.Contains("TestService")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvalidateTokenAsync_NoCaching_DoesNothing()
    {
        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = false
        };
        var provider = CreateProvider(new HttpClient(), config);

        await provider.InvalidateTokenAsync();

        // No exceptions thrown, nothing to verify
    }

    [Fact]
    public async Task GetTokenInfoAsync_Disabled_ReturnsNull()
    {
        var config = new M2MConfiguration { Enabled = false };
        var provider = CreateProvider(new HttpClient(), config);

        var info = await provider.GetTokenInfoAsync();

        info.Should().BeNull();
    }

    [Fact]
    public async Task GetTokenInfoAsync_WithCachedToken_ReturnsCachedInfo()
    {
        var tokenInfo = new TokenInfo
        {
            Token = "cached-token",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TokenType = "Bearer"
        };
        var serialized = JsonSerializer.Serialize(tokenInfo);
        var bytes = Encoding.UTF8.GetBytes(serialized);

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bytes);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = true,
            ClientId = "TestService",
            TokenCacheKeyPrefix = "m2m:token:"
        };
        var provider = CreateProvider(new HttpClient(), config, _cache);

        var info = await provider.GetTokenInfoAsync();

        info.Should().NotBeNull();
        info!.Token.Should().Be("cached-token");
    }

    [Fact]
    public void TokenInfo_IsValid_ReturnsTrueWhenNotExpired()
    {
        var info = new TokenInfo { ExpiresAt = DateTime.UtcNow.AddHours(1) };

        info.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TokenInfo_IsValid_ReturnsFalseWhenExpired()
    {
        var info = new TokenInfo { ExpiresAt = DateTime.UtcNow.AddHours(-1) };

        info.IsValid.Should().BeFalse();
    }

    [Fact]
    public void TokenInfo_ShouldRefresh_ReturnsTrueWhenInRefreshWindow()
    {
        var info = new TokenInfo { ExpiresAt = DateTime.UtcNow.AddMinutes(3) };

        info.ShouldRefresh(TimeSpan.FromMinutes(5)).Should().BeTrue();
    }

    [Fact]
    public void TokenInfo_ShouldRefresh_ReturnsFalseWhenFarFromExpiry()
    {
        var info = new TokenInfo { ExpiresAt = DateTime.UtcNow.AddHours(1) };

        info.ShouldRefresh(TimeSpan.FromMinutes(5)).Should().BeFalse();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _response;
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(string response, HttpStatusCode statusCode)
        {
            _response = response;
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            });
        }
    }
}
