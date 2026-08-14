using Girder.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class CorrelationIdMiddlewareTests
{
    private readonly ILogger<CorrelationIdMiddleware> _logger = Substitute.For<ILogger<CorrelationIdMiddleware>>();

    private CorrelationIdMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new CorrelationIdMiddleware(next, _logger);
    }

    [Fact]
    public async Task InvokeAsync_WithExistingCorrelationId_ShouldUseIt()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "existing-id-123";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be("existing-id-123");
        context.Items["CorrelationId"].Should().Be("existing-id-123");
    }

    [Fact]
    public async Task InvokeAsync_WithoutCorrelationId_ShouldUseTraceIdentifier()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        // DefaultHttpContext provides a TraceIdentifier by default

        await middleware.InvokeAsync(context);

        var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
        correlationId.Should().NotBeNullOrEmpty();
        correlationId.Should().Be(context.TraceIdentifier);
        context.Items["CorrelationId"].Should().Be(context.TraceIdentifier);
    }

    [Fact]
    public async Task InvokeAsync_WithEmptyCorrelationIdHeader_ShouldFallbackToTraceIdentifier()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "";

        await middleware.InvokeAsync(context);

        var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();
        correlationId.Should().Be(context.TraceIdentifier);
    }

    [Fact]
    public async Task InvokeAsync_ShouldStoreCorrelationIdInHttpContextItems()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "test-corr-id";

        await middleware.InvokeAsync(context);

        context.Items["CorrelationId"].Should().Be("test-corr-id");
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

        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldAddCorrelationIdToResponseHeaders()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "my-id";

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-Correlation-ID");
        context.Response.Headers["X-Correlation-ID"].ToString().Should().Be("my-id");
    }
}
