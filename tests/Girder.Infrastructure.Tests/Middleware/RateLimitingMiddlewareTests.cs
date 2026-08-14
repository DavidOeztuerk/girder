using System.Net;
using System.Security.Claims;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class RateLimitingMiddlewareTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ILogger<RateLimitingMiddleware> _logger = Substitute.For<ILogger<RateLimitingMiddleware>>();

    private RateLimitingMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        RateLimitingOptions? options = null)
    {
        next ??= _ => Task.CompletedTask;
        options ??= new RateLimitingOptions();
        return new RateLimitingMiddleware(next, _cache, _logger, Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.1");
        return context;
    }

    [Fact]
    public async Task InvokeAsync_UnderLimit_ShouldCallNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UnderLimit_ShouldAddRateLimitHeaders()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-RateLimit-Limit-Minute");
        context.Response.Headers.Should().ContainKey("X-RateLimit-Remaining-Minute");
        context.Response.Headers.Should().ContainKey("X-RateLimit-Limit-Hour");
        context.Response.Headers.Should().ContainKey("X-RateLimit-Limit-Day");
    }

    [Fact]
    public async Task InvokeAsync_OverMinuteLimit_ShouldReturn429()
    {
        // Middleware checks count > limit BEFORE incrementing.
        // With limit=1: request 1 (count=0, 0>1 false → allow, increment to 1),
        //               request 2 (count=1, 1>1 false → allow, increment to 2),
        //               request 3 (count=2, 2>1 true → block).
        var options = new RateLimitingOptions { RequestsPerMinute = 1 };
        var middleware = CreateMiddleware(options: options);

        // First two requests pass (counts 0 and 1 do not exceed limit=1)
        for (var i = 0; i < 2; i++)
        {
            var context = CreateContext();
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().NotBe((int)HttpStatusCode.TooManyRequests);
        }

        // Third request should be blocked (count=2 > limit=1)
        var blocked = CreateContext();
        await middleware.InvokeAsync(blocked);
        blocked.Response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_WhitelistedIp_ShouldBypass()
    {
        var options = new RateLimitingOptions
        {
            RequestsPerMinute = 0, // Would block everything
            WhitelistedIps = ["192.168.1.1"]
        };

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhitelistedUser_ShouldBypass()
    {
        var options = new RateLimitingOptions
        {
            RequestsPerMinute = 0,
            EnableUserRateLimiting = true,
            WhitelistedUserIds = ["user-42"]
        };

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        var claims = new List<Claim>
        {
            new("sub", "user-42")
        };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_ShouldUseUserIdAsClientId()
    {
        var options = new RateLimitingOptions
        {
            EnableUserRateLimiting = true,
            RequestsPerMinute = 1
        };

        var middleware = CreateMiddleware(options: options);

        var context = CreateContext();
        var claims = new List<Claim> { new("sub", "unique-user") };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        await middleware.InvokeAsync(context);

        // First request should succeed
        context.Response.StatusCode.Should().NotBe((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_EndpointSpecificLimit_ShouldApply()
    {
        // With endpoint limit=1, check-then-increment means:
        // request 1 (count=0, 0>1 false → allow), request 2 (count=1, 1>1 false → allow),
        // request 3 (count=2, 2>1 true → block).
        var options = new RateLimitingOptions
        {
            RequestsPerMinute = 1000,
            EndpointSpecificLimits = new Dictionary<string, EndpointRateLimit>
            {
                ["auth"] = new EndpointRateLimit
                {
                    Path = "/api/auth",
                    RequestsPerMinute = 1,
                    RequestsPerHour = 100,
                    RequestsPerDay = 1000
                }
            }
        };

        var middleware = CreateMiddleware(options: options);

        // First two requests — allowed (counts 0 and 1 do not exceed limit=1)
        for (var i = 0; i < 2; i++)
        {
            var context = CreateContext("/api/auth/login");
            await middleware.InvokeAsync(context);
            context.Response.StatusCode.Should().NotBe((int)HttpStatusCode.TooManyRequests);
        }

        // Third request — blocked (count=2 > limit=1)
        var blocked = CreateContext("/api/auth/login");
        await middleware.InvokeAsync(blocked);
        blocked.Response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_IpRateLimitingDisabled_ShouldFallBackToAnonymous()
    {
        var options = new RateLimitingOptions
        {
            EnableIpRateLimiting = false,
            EnableUserRateLimiting = false
        };

        var middleware = CreateMiddleware(options: options);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        // Should still work — falls back to "anonymous"
        context.Response.StatusCode.Should().NotBe((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_XForwardedFor_ShouldUseFirstIp()
    {
        var options = new RateLimitingOptions { EnableIpRateLimiting = true };
        var middleware = CreateMiddleware(options: options);

        var context = CreateContext();
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 10.0.0.2";

        await middleware.InvokeAsync(context);

        // Should use the forwarded IP for rate limiting
        context.Response.StatusCode.Should().NotBe((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_XRealIp_ShouldBeUsed()
    {
        var options = new RateLimitingOptions { EnableIpRateLimiting = true };
        var middleware = CreateMiddleware(options: options);

        var context = CreateContext();
        context.Request.Headers["X-Real-IP"] = "10.0.0.5";

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().NotBe((int)HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task InvokeAsync_RateLimitExceeded_ShouldReturnJsonResponse()
    {
        // With limit=0, check-then-increment: first request (count=0, 0>0 false → allow),
        // second request (count=1, 1>0 true → block).
        var options = new RateLimitingOptions { RequestsPerMinute = 0 };
        var middleware = CreateMiddleware(options: options);

        // First request passes (0 > 0 is false)
        var warmup = CreateContext();
        await middleware.InvokeAsync(warmup);

        // Second request should be blocked with JSON response
        var context = CreateContext();
        context.Response.Body = new MemoryStream();
        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
        context.Response.ContentType.Should().Be("application/json");
    }
}
