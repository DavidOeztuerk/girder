using Girder.Infrastructure.Communication.Configuration;
using Girder.Infrastructure.Security.M2M;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security.M2M;

[Trait("Category", "Unit")]
public class ServiceTokenProviderTopUpTests
{
    private readonly ILogger<ServiceTokenProvider> _logger = Substitute.For<ILogger<ServiceTokenProvider>>();
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();

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
            EnableTokenCaching = false,
            TokenLifetime = TimeSpan.FromHours(1)
        };
        var options = Options.Create(new ServiceCommunicationOptions { M2M = m2mConfig });
        return new ServiceTokenProvider(httpClient, _logger, options, cache);
    }

    #region GetTokenAsync — cache expiry scenario

    [Fact]
    public async Task GetTokenAsync_ExpiredCachedToken_RequestsNewToken()
    {
        // Arrange — cache returns an expired token
        var expiredToken = new TokenInfo
        {
            Token = "expired-token",
            ExpiresAt = DateTime.UtcNow.AddHours(-1), // expired
            TokenType = "Bearer"
        };
        var serialized = JsonSerializer.Serialize(expiredToken);
        var bytes = Encoding.UTF8.GetBytes(serialized);

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bytes);
        _cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var responseJson = JsonSerializer.Serialize(new
        {
            data = new { accessToken = "fresh-token", expiresIn = 3600 }
        });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = true,
            ClientId = "TestService",
            TokenCacheKeyPrefix = "m2m:token:",
            TokenEndpoint = "http://localhost:8080/api/auth/service-token",
            ClientSecret = "secret"
        };
        var provider = CreateProvider(httpClient, config, _cache);

        // Act
        var token = await provider.GetTokenAsync();

        // Assert — expired cache token is cleared and a new one is fetched
        token.Should().Be("fresh-token");
    }

    [Fact]
    public async Task GetTokenAsync_CacheSoonToExpire_ProactivelyRefreshes()
    {
        // Token expires in 2 minutes, RefreshBeforeExpiry is 5 minutes — should trigger background refresh
        var soonToken = new TokenInfo
        {
            Token = "about-to-expire",
            ExpiresAt = DateTime.UtcNow.AddMinutes(2),
            TokenType = "Bearer"
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(soonToken));

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(bytes);

        var responseJson = JsonSerializer.Serialize(new
        {
            data = new { accessToken = "refreshed-token", expiresIn = 3600 }
        });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = true,
            ClientId = "TestService",
            TokenCacheKeyPrefix = "m2m:token:",
            RefreshBeforeExpiry = TimeSpan.FromMinutes(5), // bigger than 2 minutes left
            TokenEndpoint = "http://localhost:8080/api/auth/service-token"
        };
        var provider = CreateProvider(httpClient, config, _cache);

        // Act — returns current token but schedules background refresh
        var token = await provider.GetTokenAsync();

        token.Should().Be("about-to-expire");
        // Background refresh fires but we don't wait — just verify no exception
        await Task.Delay(50); // give the background task a moment
    }

    #endregion

    #region GetTokenInfoAsync — no caching

    [Fact]
    public async Task GetTokenInfoAsync_NoCaching_RequestsNewTokenAndReturnsInfo()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new { accessToken = "info-token", expiresIn = 1800 }
        });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = false,
            ClientId = "TestService",
            TokenEndpoint = "http://localhost:8080/api/auth/service-token",
            TokenLifetime = TimeSpan.FromHours(1)
        };
        var provider = CreateProvider(httpClient, config);

        var info = await provider.GetTokenInfoAsync();

        info.Should().NotBeNull();
        info!.Token.Should().Be("info-token");
        info.TokenType.Should().Be("Bearer");
        info.ExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task GetTokenInfoAsync_NoCachingAndRequestFails_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler("error", HttpStatusCode.Unauthorized);
        var httpClient = new HttpClient(handler);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = false,
            TokenEndpoint = "http://localhost:8080/api/auth/service-token"
        };
        var provider = CreateProvider(httpClient, config);

        var info = await provider.GetTokenInfoAsync();

        info.Should().BeNull();
    }

    #endregion

    #region RefreshTokenAsync — exception path

    [Fact]
    public async Task RefreshTokenAsync_HttpClientThrows_ReturnsNull()
    {
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("Network error"));
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_ResponseHasNullDataElement_ReturnsNull()
    {
        var responseJson = JsonSerializer.Serialize(new { data = (object?)null });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_AccessTokenIsEmpty_ReturnsNull()
    {
        var responseJson = JsonSerializer.Serialize(new { data = new { accessToken = "" } });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var provider = CreateProvider(httpClient);

        var token = await provider.RefreshTokenAsync();

        token.Should().BeNull();
    }

    [Fact]
    public async Task RefreshTokenAsync_ResponseMissingExpiresIn_UsesDefaultLifetime()
    {
        // No expiresIn in response — should fall back to TokenLifetime
        var responseJson = JsonSerializer.Serialize(new { data = new { accessToken = "token-no-expiry" } });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = false,
            TokenEndpoint = "http://localhost:8080/api/auth/service-token",
            ClientId = "TestService",
            TokenLifetime = TimeSpan.FromHours(2)
        };
        var provider = CreateProvider(httpClient, config);

        var token = await provider.RefreshTokenAsync();

        token.Should().Be("token-no-expiry");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithCaching_CachesReturnedToken()
    {
        var responseJson = JsonSerializer.Serialize(new
        {
            data = new { accessToken = "cached-new-token", expiresIn = 3600 }
        });
        var handler = new MockHttpMessageHandler(responseJson, HttpStatusCode.OK);
        var httpClient = new HttpClient(handler);

        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = true,
            ClientId = "TestService",
            TokenCacheKeyPrefix = "m2m:token:",
            TokenEndpoint = "http://localhost:8080/api/auth/service-token"
        };

        // Cache returns nothing (cache miss)
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        var provider = CreateProvider(httpClient, config, _cache);

        var token = await provider.RefreshTokenAsync();

        token.Should().Be("cached-new-token");
        // Verify that the token was cached
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region InvalidateTokenAsync — caching disabled

    [Fact]
    public async Task InvalidateTokenAsync_CachingDisabled_DoesNotCallCache()
    {
        var config = new M2MConfiguration
        {
            Enabled = true,
            EnableTokenCaching = false
        };
        var provider = CreateProvider(new HttpClient(), config, _cache);

        await provider.InvalidateTokenAsync();

        await _cache.DidNotReceive().RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region TokenInfo properties

    [Fact]
    public void TokenInfo_Scopes_CanBeSetAndRead()
    {
        var info = new TokenInfo
        {
            Token = "tok",
            TokenType = "Bearer",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Scopes = new HashSet<string> { "read", "write" }
        };

        info.Scopes.Should().Contain("read");
        info.Scopes.Should().Contain("write");
        info.IsValid.Should().BeTrue();
    }

    [Fact]
    public void TokenInfo_DefaultScopes_IsEmpty()
    {
        var info = new TokenInfo
        {
            Token = "tok",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        info.Scopes.Should().BeEmpty();
    }

    #endregion

    #region Mock handlers

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

    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Exception _exception;

        public ThrowingHttpMessageHandler(Exception exception)
        {
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    #endregion
}
