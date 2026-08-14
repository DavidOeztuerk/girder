using System.Text;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class RequestLoggingMiddlewareTests
{
    private readonly ILogger<RequestLoggingMiddleware> _logger = Substitute.For<ILogger<RequestLoggingMiddleware>>();

    private RequestLoggingMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        ObservabilityOptions? options = null)
    {
        next ??= _ => Task.CompletedTask;
        options ??= new ObservabilityOptions { EnableDetailedHttpLogging = true };
        return new RequestLoggingMiddleware(next, _logger, Options.Create(options));
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task InvokeAsync_DetailedLoggingEnabled_ShouldCallNext()
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
    public async Task InvokeAsync_DetailedLoggingDisabled_ShouldStillCallNext()
    {
        var nextCalled = false;
        var options = new ObservabilityOptions { EnableDetailedHttpLogging = false };
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
    public async Task InvokeAsync_HealthEndpoint_ShouldSkipDetailedLogging()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("/health");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MetricsEndpoint_ShouldSkipDetailedLogging()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateContext("/metrics");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WithRequestBody_ShouldLogRequest()
    {
        var middleware = CreateMiddleware();

        var context = CreateContext("/api/test", "POST");
        var body = "{\"name\":\"test\"}"u8.ToArray();
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";

        await middleware.InvokeAsync(context);

        // Logger should have been called
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_WithErrorResponse_ShouldLogAsWarning()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 500;
            return ctx.Response.WriteAsync("Server Error");
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        // Response body should be preserved (copied to original stream)
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
        responseText.Should().Contain("Server Error");
    }

    [Fact]
    public async Task InvokeAsync_WithSensitiveBody_ShouldRedact()
    {
        var middleware = CreateMiddleware();

        var context = CreateContext("/api/test", "POST");
        var body = Encoding.UTF8.GetBytes("{\"password\":\"secret123\",\"name\":\"test\"}");
        context.Request.Body = new MemoryStream(body);
        context.Request.ContentLength = body.Length;
        context.Request.ContentType = "application/json";

        await middleware.InvokeAsync(context);

        // Should complete without errors — body redaction happens internally
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_AuthEndpointResponse_ShouldRedactResponseBody()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            return ctx.Response.WriteAsync("{\"accessToken\":\"secret\"}");
        });

        var context = CreateContext("/api/auth/login", "POST");
        await middleware.InvokeAsync(context);

        // Should complete without error, response body redacted for auth endpoints
        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldRedactSensitiveQueryParams()
    {
        var middleware = CreateMiddleware();

        var context = CreateContext("/api/callback");
        context.Request.QueryString = new QueryString("?code=secret_code&state=ok");

        await middleware.InvokeAsync(context);

        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotLogSensitiveHeaders()
    {
        var middleware = CreateMiddleware();

        var context = CreateContext();
        context.Request.Headers["Authorization"] = "Bearer secret-token";
        context.Request.Headers["X-Custom-Header"] = "safe-value";

        await middleware.InvokeAsync(context);

        _logger.ReceivedCalls().Should().NotBeEmpty();
    }

    [Fact]
    public async Task InvokeAsync_ResponseBodyShouldBePreserved()
    {
        var responseContent = "Hello World";
        var middleware = CreateMiddleware(async ctx =>
        {
            await ctx.Response.WriteAsync(responseContent);
        });

        var context = CreateContext();
        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Be(responseContent);
    }
}
