using Girder.Abstractions.Caching;
using System.Net;
using System.Security.Claims;
using Girder.Infrastructure.Caching;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class DistributedRateLimitingMiddlewareTests
{
    private readonly IDistributedRateLimitStore _rateLimitStore = Substitute.For<IDistributedRateLimitStore>();
    private readonly ILogger<DistributedRateLimitingMiddleware> _logger = Substitute.For<ILogger<DistributedRateLimitingMiddleware>>();

    private DistributedRateLimitingMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        DistributedRateLimitingOptions? options = null)
    {
        next ??= _ => Task.CompletedTask;
        options ??= new DistributedRateLimitingOptions
        {
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };
        return new DistributedRateLimitingMiddleware(next, _rateLimitStore, _logger, Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.1");
        return context;
    }

    [Fact]
    public async Task InvokeAsync_Disabled_ShouldCallNextWithoutChecks()
    {
        var nextCalled = false;
        var options = new DistributedRateLimitingOptions
        {
            Enabled = false,
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        await _rateLimitStore.DidNotReceive().SlidingWindowIncrementAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_UnderLimit_ShouldCallNext()
    {
        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = true, CurrentCount = 1, Limit = 100 });

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
    public async Task InvokeAsync_OverLimit_ShouldReturn429()
    {
        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = false, CurrentCount = 101, Limit = 100 });

        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.TooManyRequests);
        context.Response.ContentType.Should().Be("application/json");
    }

    [Fact]
    public async Task InvokeAsync_WhitelistedIp_ShouldBypass()
    {
        var options = new DistributedRateLimitingOptions
        {
            WhitelistedIps = ["10.0.0.1"],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
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
        // Store should not be called
        await _rateLimitStore.DidNotReceive().SlidingWindowIncrementAsync(
            Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_WhitelistedUser_ShouldBypass()
    {
        var options = new DistributedRateLimitingOptions
        {
            EnableUserRateLimiting = true,
            WhitelistedIps = [],
            WhitelistedUserIds = ["user-42"],
            WhitelistedEndpoints = []
        };

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-42")], "TestAuth"));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhitelistedEndpoint_ShouldBypass()
    {
        var options = new DistributedRateLimitingOptions
        {
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = ["GET:/api/test"]
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
    public async Task InvokeAsync_AuthenticatedUser_ShouldUseUserIdAsKey()
    {
        var options = new DistributedRateLimitingOptions
        {
            EnableUserRateLimiting = true,
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };

        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = true, CurrentCount = 1, Limit = 100 });

        var middleware = CreateMiddleware(options: options);
        var context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-abc")], "TestAuth"));

        await middleware.InvokeAsync(context);

        // Verify that the key used contains the user ID
        await _rateLimitStore.Received().SlidingWindowIncrementAsync(
            Arg.Is<string>(k => k.Contains("user:user-abc")),
            Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_EndpointSpecificLimit_ShouldNotReplaceGlobalClientLimits()
    {
        var options = new DistributedRateLimitingOptions
        {
            RequestsPerMinute = 100,
            RequestsPerHour = 1000,
            RequestsPerDay = 10000,
            EndpointSpecificLimits = new Dictionary<string, EndpointRateLimit>
            {
                {
                    "/api/contact",
                    new EndpointRateLimit
                    {
                        RequestsPerMinute = 3,
                        RequestsPerHour = 10,
                        RequestsPerDay = 30
                    }
                }
            },
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };

        _rateLimitStore
            .SlidingWindowIncrementAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult
            {
                IsAllowed = true,
                CurrentCount = 1,
                Limit = 100
            });

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, options);
        var context = CreateContext("/api/contact", "POST");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        await _rateLimitStore.Received().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains(":min:") && !key.Contains(":endpoint:")),
            100,
            TimeSpan.FromMinutes(1),
            Arg.Any<CancellationToken>());
        await _rateLimitStore.Received().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains(":endpoint:POST:/api/contact:min:")),
            3,
            TimeSpan.FromMinutes(1),
            Arg.Any<CancellationToken>());
        await _rateLimitStore.DidNotReceive().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains(":min:") && !key.Contains(":endpoint:")),
            3,
            TimeSpan.FromMinutes(1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_FixedWindow_ShouldUseIncrementAsync()
    {
        var options = new DistributedRateLimitingOptions
        {
            UseSlidingWindow = false,
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };

        _rateLimitStore.IncrementAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(1L);

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        await _rateLimitStore.Received().IncrementAsync(
            Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_StoreThrows_ShouldAllowRequest()
    {
        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<WindowCheckResult>(x => throw new Exception("Redis down"));

        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("should allow on error to prevent service disruption");
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddRateLimitHeader()
    {
        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = true, CurrentCount = 1, Limit = 100 });

        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-RateLimit-Limit");
    }

    [Fact]
    public async Task InvokeAsync_OverLimit_ShouldAddRetryAfterHeader()
    {
        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = false, CurrentCount = 101, Limit = 100 });

        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task InvokeAsync_XForwardedFor_DoesNotBuyAPlaceOnTheAllowList()
    {
        var options = new DistributedRateLimitingOptions
        {
            EnableIpRateLimiting = true,
            WhitelistedIps = ["203.0.113.1"],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };

        _rateLimitStore.SlidingWindowIncrementAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = true });

        var middleware = CreateMiddleware(options: options);
        var context = CreateContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.1";

        await middleware.InvokeAsync(context);

        await _rateLimitStore.Received().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains("10.0.0.1")),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await _rateLimitStore.DidNotReceive().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains("203.0.113.1")),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_XRealIp_DoesNotBuyAPlaceOnTheAllowList()
    {
        var options = new DistributedRateLimitingOptions
        {
            EnableIpRateLimiting = true,
            WhitelistedIps = ["203.0.113.5"],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };

        _rateLimitStore.SlidingWindowIncrementAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = true });

        var middleware = CreateMiddleware(options: options);
        var context = CreateContext();
        context.Request.Headers["X-Real-IP"] = "203.0.113.5";

        await middleware.InvokeAsync(context);

        await _rateLimitStore.Received().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains("10.0.0.1")),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await _rateLimitStore.DidNotReceive().SlidingWindowIncrementAsync(
            Arg.Is<string>(key => key.Contains("203.0.113.5")),
            Arg.Any<int>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AnonymousUser_IpDisabled_ShouldFallbackToAnonymous()
    {
        var options = new DistributedRateLimitingOptions
        {
            EnableIpRateLimiting = false,
            EnableUserRateLimiting = false,
            WhitelistedIps = [],
            WhitelistedUserIds = [],
            WhitelistedEndpoints = []
        };

        _rateLimitStore.SlidingWindowIncrementAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new WindowCheckResult { IsAllowed = true, CurrentCount = 1, Limit = 100 });

        var middleware = CreateMiddleware(options: options);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        // Should use "anonymous" as key prefix
        await _rateLimitStore.Received().SlidingWindowIncrementAsync(
            Arg.Is<string>(k => k.Contains("anonymous")),
            Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }
}
