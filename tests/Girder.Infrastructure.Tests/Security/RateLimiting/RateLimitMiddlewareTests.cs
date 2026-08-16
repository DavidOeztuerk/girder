using Girder.Abstractions.Security.Audit;
using Girder.Infrastructure.Security.Monitoring;
using Girder.Infrastructure.Security.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Girder.Infrastructure.Tests.Security.RateLimiting;

[Trait("Category", "Unit")]
public class RateLimitMiddlewareTests
{
    private readonly IRateLimitService _rateLimitService = Substitute.For<IRateLimitService>();
    private readonly ILogger<RateLimitMiddleware> _logger = Substitute.For<ILogger<RateLimitMiddleware>>();

    private RateLimitMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        RateLimitOptions? options = null)
    {
        var opts = Options.Create(options ?? new RateLimitOptions());
        next ??= _ => Task.CompletedTask;
        return new RateLimitMiddleware(next, _rateLimitService, _logger, opts);
    }

    private static DefaultHttpContext CreateContext(
        string path = "/api/test",
        string method = "GET",
        string? userId = null,
        string? apiKey = null,
        string? forwardedFor = null,
        string? realIp = null,
        string? userAgent = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        // Register services including ISecurityAlertService mock
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISecurityAlertService>());
        context.RequestServices = services.BuildServiceProvider();

        if (userId != null)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test");
            context.User = new ClaimsPrincipal(identity);
        }

        if (apiKey != null)
        {
            context.Request.Headers["X-API-Key"] = apiKey;
        }

        if (forwardedFor != null)
        {
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        }

        if (realIp != null)
        {
            context.Request.Headers["X-Real-IP"] = realIp;
        }

        if (userAgent != null)
        {
            context.Request.Headers["User-Agent"] = userAgent;
        }

        return context;
    }

    #region ShouldSkip paths

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/metrics")]
    [InlineData("/swagger/index.html")]
    [InlineData("/favicon.ico")]
    public async Task InvokeAsync_SkippedPath_DoesNotCallRateLimitService(string path)
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        await _rateLimitService.DidNotReceive().CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("/api/test.css")]
    [InlineData("/api/app.js")]
    [InlineData("/api/logo.png")]
    [InlineData("/api/bg.jpg")]
    [InlineData("/api/anim.gif")]
    public async Task InvokeAsync_StaticFile_DoesNotCallRateLimitService(string path)
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        await _rateLimitService.DidNotReceive().CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_OptionsRequest_DoesNotCallRateLimitService()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", "OPTIONS");

        await middleware.InvokeAsync(context);

        await _rateLimitService.DidNotReceive().CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_DisabledRateLimiting_DoesNotCallRateLimitService()
    {
        var options = new RateLimitOptions { EnableRateLimiting = false };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/api/users");

        await middleware.InvokeAsync(context);

        await _rateLimitService.DidNotReceive().CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Rate limit allowed

    [Fact]
    public async Task InvokeAsync_Allowed_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_Returns429()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Too many requests",
                Limit = 10,
                CurrentCount = 11,
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(429);
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_DoesNotCallNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
    }

    #endregion

    #region Fail-open / fail-closed

    [Fact]
    public async Task InvokeAsync_ServiceThrows_FailOpen_CallsNext()
    {
        var nextCalled = false;
        var options = new RateLimitOptions { FailOpen = true };
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; }, options: options);
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("service down"));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ServiceThrows_FailClosed_Returns503()
    {
        var options = new RateLimitOptions { FailOpen = false };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("service down"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(503);
    }

    #endregion

    #region ExcludedPaths option

    [Fact]
    public async Task InvokeAsync_ExcludedPath_DoesNotCallRateLimitService()
    {
        var options = new RateLimitOptions { ExcludedPaths = new List<string> { "/internal" } };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/internal/status");

        await middleware.InvokeAsync(context);

        await _rateLimitService.DidNotReceive().CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Headers

    [Fact]
    public async Task InvokeAsync_Allowed_AddsRateLimitPolicyHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() });

        await middleware.InvokeAsync(context);

        context.Response.Headers.ContainsKey("X-RateLimit-Policy").Should().BeTrue();
    }

    #endregion

    #region UseRateLimit extension

    [Fact]
    public void UseRateLimit_RegistersMiddleware()
    {
        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(
            new ServiceCollection().BuildServiceProvider());

        var act = () => app.UseRateLimit();

        act.Should().NotThrow();
    }

    #endregion

    #region Coverage Tests

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_ClientIdUsesUserId()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", userId: "user-42");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured.Should().NotBeNull();
        captured!.ClientId.Should().StartWith("user:");
    }

    [Fact]
    public async Task InvokeAsync_ApiKeyOnly_ClientIdUsesApiKey()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", apiKey: "my-api-key");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured.Should().NotBeNull();
        captured!.ClientId.Should().StartWith("apikey:");
    }

    [Fact]
    public async Task InvokeAsync_NoAuthNoApiKey_ClientIdUsesIp()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test");
        // Set remote IP
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured.Should().NotBeNull();
        captured!.ClientId.Should().StartWith("ip:");
    }

    [Fact]
    public async Task InvokeAsync_XForwardedFor_UsesFirstIp()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", forwardedFor: "1.2.3.4, 5.6.7.8");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.IpAddress.Should().Be("1.2.3.4");
    }

    [Fact]
    public async Task InvokeAsync_XRealIp_UsesRealIp()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", realIp: "9.8.7.6");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.IpAddress.Should().Be("9.8.7.6");
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_WithRetryAfter_SetsRetryAfterHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Too many",
                Limit = 10,
                CurrentCount = 15,
                RetryAfter = TimeSpan.FromSeconds(30),
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(429);
        context.Response.Headers.ContainsKey("Retry-After").Should().BeTrue();
        context.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_WithCustomStatusCode_UsesCustomCode()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Blocked",
                TriggeredRule = new RateLimitRule
                {
                    Name = "block-rule",
                    Actions = new RateLimitActions { CustomStatusCode = 403 }
                },
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_WithCustomHeaders_AddsHeaders()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                TriggeredRule = new RateLimitRule
                {
                    Name = "test-rule",
                    Actions = new RateLimitActions
                    {
                        ResponseHeaders = new Dictionary<string, string>
                        {
                            ["X-Custom-Header"] = "blocked"
                        }
                    }
                },
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Custom-Header"].ToString().Should().Be("blocked");
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_SendsSecurityAlert()
    {
        var alertService = Substitute.For<ISecurityAlertService>();
        var services = new ServiceCollection();
        services.AddSingleton(alertService);

        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");
        context.RequestServices = services.BuildServiceProvider();

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Exceeded",
                Severity = RateLimitSeverity.Critical,
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        await alertService.Received(1).SendAlertAsync(
            SecurityAlertLevel.Critical,
            SecurityAlertType.RateLimitExceeded,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_GuidSegment_NormalizedToIdPlaceholder()
    {
        var middleware = CreateMiddleware();
        var guid = Guid.NewGuid().ToString();
        var context = CreateContext($"/api/users/{guid}");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Endpoint.Should().Contain("{id}");
        captured.Endpoint.Should().NotContain(guid);
    }

    [Fact]
    public async Task InvokeAsync_NumericIdSegment_NormalizedToIdPlaceholder()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/orders/12345");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Endpoint.Should().Contain("{id}");
    }

    [Fact]
    public async Task InvokeAsync_BotUserAgent_CategorizedAsBot()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", userAgent: "Googlebot/2.1");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Metadata.Should().ContainKey("user_agent_category");
        captured.Metadata["user_agent_category"].Should().Be("bot");
    }

    [Fact]
    public async Task InvokeAsync_MobileUserAgent_CategorizedAsMobile()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", userAgent: "Mozilla/5.0 (iPhone; CPU iPhone OS 14_0 like Mac OS X)");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Metadata["user_agent_category"].Should().Be("mobile");
    }

    [Fact]
    public async Task InvokeAsync_CurlUserAgent_CategorizedAsApiClient()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", userAgent: "curl/7.68.0");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Metadata["user_agent_category"].Should().Be("api_client");
    }

    [Fact]
    public async Task InvokeAsync_ChromeUserAgent_CategorizedAsBrowser()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", userAgent: "Mozilla/5.0 Chrome/100.0");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Metadata["user_agent_category"].Should().Be("browser");
    }

    [Fact]
    public async Task InvokeAsync_WithContentLength_CategorizesRequestSize()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", method: "POST");
        context.Request.ContentLength = 500; // small
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Metadata.Should().ContainKey("request_size_category");
        captured.Metadata["request_size_category"].Should().Be("small");
    }

    [Fact]
    public async Task InvokeAsync_WithContentType_IncludesInMetadata()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", method: "POST");
        context.Request.ContentType = "application/json; charset=utf-8";
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.Metadata["content_type"].Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_LogSuccessfulRequests_DoesNotThrow()
    {
        var options = new RateLimitOptions { LogSuccessfulRequests = true };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().NotBe(429);
    }

    [Fact]
    public async Task InvokeAsync_Allowed_WithTriggeredRule_AddsRuleHeader()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = true,
                TriggeredRule = new RateLimitRule { Name = "standard-rule" },
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-RateLimit-Rule"].ToString().Should().Be("standard-rule");
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_IncludeRuleDetails_IncludesRuleInResponse()
    {
        var options = new RateLimitOptions { IncludeRuleDetails = true };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Exceeded",
                TriggeredRule = new RateLimitRule
                {
                    Id = "rule-1",
                    Name = "api-limit",
                    Description = "API rate limit",
                    Actions = new RateLimitActions()
                },
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(429);
        // Response body should contain rule details
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("api-limit");
    }

    [Fact]
    public async Task InvokeAsync_IcoFile_DoesNotCallRateLimitService()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/favicon.ico");

        await middleware.InvokeAsync(context);

        await _rateLimitService.DidNotReceive().CheckRateLimitAsync(
            Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_BearerToken_ClientIdUsesApiKey()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test");
        context.Request.Headers.Authorization = "Bearer my-bearer-token";
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.ClientId.Should().StartWith("apikey:");
    }

    [Fact]
    public async Task InvokeAsync_ApiKeyInQueryParam_ClientIdUsesApiKey()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test");
        context.Request.QueryString = new QueryString("?api_key=query-key-123");
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        captured!.ClientId.Should().StartWith("apikey:");
    }

    [Fact]
    public async Task InvokeAsync_NoUserNoApiKeyNoIp_FallsBackToIpUnknown()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test");
        // No userId, no API key, no remote IP, no forwarded headers
        RateLimitRequest? captured = null;

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                captured = ci.Arg<RateLimitRequest>();
                return new RateLimitResult { IsAllowed = true, Headers = new Dictionary<string, string>() };
            });

        await middleware.InvokeAsync(context);

        // GetClientIpAddress returns "unknown" when no remote IP, so GetClientId returns "ip:unknown"
        captured!.ClientId.Should().Be("ip:unknown");
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_WithResponseDelay_DelaysResponse()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Delayed",
                TriggeredRule = new RateLimitRule
                {
                    Name = "delay-rule",
                    Actions = new RateLimitActions
                    {
                        ResponseDelay = TimeSpan.FromMilliseconds(50)
                    }
                },
                Headers = new Dictionary<string, string>()
            });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await middleware.InvokeAsync(context);
        sw.Stop();

        context.Response.StatusCode.Should().Be(429);
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_WithCustomAction_ExecutesAction()
    {
        var actionExecuted = false;
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Custom action",
                TriggeredRule = new RateLimitRule
                {
                    Name = "custom-action-rule",
                    Actions = new RateLimitActions
                    {
                        CustomAction = (_, _) => { actionExecuted = true; return Task.CompletedTask; }
                    }
                },
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        actionExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_WithAuditService_LogsToAudit()
    {
        var auditService = Substitute.For<Girder.Abstractions.Security.Audit.ISecurityAuditService>();
        auditService.LogSecurityEventAsync(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<Girder.Abstractions.Security.Audit.SecurityEventSeverity>(),
                Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns("event-1");

        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ISecurityAlertService>());
        services.AddSingleton(auditService);

        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");
        context.RequestServices = services.BuildServiceProvider();

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Audit test",
                Severity = RateLimitSeverity.Warning,
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        await auditService.Received(1).LogSecurityEventAsync(
            "RateLimitExceeded",
            Arg.Any<string>(),
            Girder.Abstractions.Security.Audit.SecurityEventSeverity.Medium,
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_SevereSeverity_MapsToHighAlert()
    {
        var alertService = Substitute.For<ISecurityAlertService>();
        var services = new ServiceCollection();
        services.AddSingleton(alertService);

        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users");
        context.RequestServices = services.BuildServiceProvider();

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .Returns(new RateLimitResult
            {
                IsAllowed = false,
                Reason = "Severe",
                Severity = RateLimitSeverity.Severe,
                Headers = new Dictionary<string, string>()
            });

        await middleware.InvokeAsync(context);

        await alertService.Received(1).SendAlertAsync(
            SecurityAlertLevel.High,
            SecurityAlertType.RateLimitExceeded,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Dictionary<string, object>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_RateLimitServiceThrows_FailOpen_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; },
            options: new RateLimitOptions { FailOpen = true });
        var context = CreateContext("/api/test");

        _rateLimitService.CheckRateLimitAsync(Arg.Any<RateLimitRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Service down"));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    #endregion
}
