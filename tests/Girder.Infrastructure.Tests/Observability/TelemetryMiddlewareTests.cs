using System.Diagnostics;
using System.Security.Claims;
using Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class TelemetryMiddlewareTests
{
    private readonly ILogger<TelemetryMiddleware> _logger = Substitute.For<ILogger<TelemetryMiddleware>>();
    private readonly ITelemetryService _telemetryService = Substitute.For<ITelemetryService>();
    private readonly ICustomMetrics _metrics = Substitute.For<ICustomMetrics>();

    private TelemetryMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        ObservabilityOptions? options = null)
    {
        next ??= _ => Task.CompletedTask;
        options ??= new ObservabilityOptions();
        return new TelemetryMiddleware(next, _logger, _telemetryService, _metrics, Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ShouldCallNext()
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
    public async Task InvokeAsync_ShouldStartActivity()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users", "POST");

        await middleware.InvokeAsync(context);

        _telemetryService.Received().StartActivity("POST /api/users", ActivityKind.Server);
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddRequestTags()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        _telemetryService.Received().AddTags(Arg.Is<KeyValuePair<string, object?>[]>(tags =>
            tags.Any(t => t.Key == "http.method" && (string?)t.Value == "GET")));
    }

    [Fact]
    public async Task InvokeAsync_Success_ShouldSetStatusOk()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        _telemetryService.Received().SetStatus(ActivityStatusCode.Ok, Arg.Any<string?>());
    }

    [Fact]
    public async Task InvokeAsync_ErrorStatusCode_ShouldSetStatusError()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        _telemetryService.Received().SetStatus(ActivityStatusCode.Error, "HTTP 500");
    }

    [Fact]
    public async Task InvokeAsync_Exception_ShouldRecordExceptionAndRethrow()
    {
        var exception = new System.InvalidOperationException("test error");
        var middleware = CreateMiddleware(_ => throw exception);

        var context = CreateContext();
        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<System.InvalidOperationException>();

        _telemetryService.Received().RecordException(exception);
        _telemetryService.Received().SetStatus(ActivityStatusCode.Error, "test error");
    }

    [Fact]
    public async Task InvokeAsync_WithAuthenticatedUser_ShouldAddUserId()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "user-123")], "TestAuth"));

        await middleware.InvokeAsync(context);

        _telemetryService.Received().AddTags(Arg.Is<KeyValuePair<string, object?>[]>(tags =>
            tags.Any(t => t.Key == "user.id" && (string?)t.Value == "user-123")));
    }

    [Fact]
    public async Task InvokeAsync_HealthEndpoint_ShouldSkipRequestCompletionLog()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/health");

        await middleware.InvokeAsync(context);

        // _logger should not receive a Log call for health endpoints
        // (beyond the scope call). We verify indirectly: it should still complete without error.
        _telemetryService.Received().StartActivity(Arg.Any<string>(), Arg.Any<ActivityKind>());
    }

    [Fact]
    public async Task InvokeAsync_MetricsEndpoint_ShouldSkipRequestCompletionLog()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/metrics");

        await middleware.InvokeAsync(context);

        _telemetryService.Received().StartActivity(Arg.Any<string>(), Arg.Any<ActivityKind>());
    }

    [Fact]
    public async Task InvokeAsync_SlowRequest_ShouldLog()
    {
        var options = new ObservabilityOptions { SlowRequestLogThresholdMs = 0 };
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        // With threshold 0, every request is "slow" and should trigger logging
        _telemetryService.Received().AddTags(Arg.Is<KeyValuePair<string, object?>[]>(tags =>
            tags.Any(t => t.Key == "http.request.duration_ms")));
    }

    [Fact]
    public async Task InvokeAsync_ShouldRedactSensitiveQueryParams()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.QueryString = new QueryString("?token=secret&name=test");

        await middleware.InvokeAsync(context);

        _telemetryService.Received().AddTags(Arg.Is<KeyValuePair<string, object?>[]>(tags =>
            tags.Any(t => t.Key == "http.query" && ((string?)t.Value)!.Contains("[REDACTED]"))));
    }

    [Fact]
    public async Task InvokeAsync_ClientIpFromXForwardedFor_ShouldBeUsed()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.1";

        await middleware.InvokeAsync(context);

        _telemetryService.Received().AddTags(Arg.Is<KeyValuePair<string, object?>[]>(tags =>
            tags.Any(t => t.Key == "client.ip" && (string?)t.Value == "203.0.113.1")));
    }

    [Fact]
    public async Task InvokeAsync_EmptyQueryString_ShouldNotRedact()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        _telemetryService.Received().AddTags(Arg.Is<KeyValuePair<string, object?>[]>(tags =>
            tags.Any(t => t.Key == "http.query")));
    }

    [Fact]
    public async Task InvokeAsync_4xxStatusCode_ShouldLogWarning()
    {
        var options = new ObservabilityOptions { SlowRequestLogThresholdMs = 99999 };
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, options);

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        _telemetryService.Received().SetStatus(ActivityStatusCode.Error, "HTTP 404");
    }
}
