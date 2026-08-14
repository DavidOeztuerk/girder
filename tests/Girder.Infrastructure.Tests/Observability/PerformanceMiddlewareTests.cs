using Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class PerformanceMiddlewareTests
{
    private readonly IPerformanceMetrics _performanceMetrics = Substitute.For<IPerformanceMetrics>();
    private readonly ILogger<PerformanceMiddleware> _logger = Substitute.For<ILogger<PerformanceMiddleware>>();

    private PerformanceMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new PerformanceMiddleware(next, _performanceMetrics, _logger);
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
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldRecordPerformanceMetrics()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        _performanceMetrics.Received(1).RecordRequest(
            "GET",
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<double>());
    }

    [Fact]
    public async Task InvokeAsync_ShouldRecordStatusCode()
    {
        var middleware = CreateMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 404;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        _performanceMetrics.Received(1).RecordRequest(
            "GET",
            Arg.Any<string>(),
            404,
            Arg.Any<double>());
    }

    [Fact]
    public async Task InvokeAsync_SlowRequest_ShouldLogWarning()
    {
        var middleware = CreateMiddleware(async _ =>
        {
            await Task.Delay(1100); // Over 1 second threshold
        });

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/slow";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        _performanceMetrics.Received(1).RecordRequest(
            "GET",
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Is<double>(d => d > 1000));
    }

    [Fact]
    public async Task InvokeAsync_NextThrows_ShouldStillRecordMetrics()
    {
        var middleware = CreateMiddleware(_ => throw new System.InvalidOperationException("boom"));

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        context.Request.Method = "POST";

        var act = () => middleware.InvokeAsync(context);
        await act.Should().ThrowAsync<System.InvalidOperationException>();

        _performanceMetrics.Received(1).RecordRequest(
            "POST",
            Arg.Any<string>(),
            Arg.Any<int>(),
            Arg.Any<double>());
    }

    [Fact]
    public async Task InvokeAsync_PathWithGuid_ShouldNormalize()
    {
        var middleware = CreateMiddleware();
        var guid = Guid.NewGuid().ToString();
        var context = new DefaultHttpContext();
        context.Request.Path = $"/api/users/{guid}";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        _performanceMetrics.Received(1).RecordRequest(
            "GET",
            "/api/users/{id}",
            Arg.Any<int>(),
            Arg.Any<double>());
    }

    [Fact]
    public async Task InvokeAsync_PathWithNumericId_ShouldNormalize()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/items/12345";
        context.Request.Method = "DELETE";

        await middleware.InvokeAsync(context);

        _performanceMetrics.Received(1).RecordRequest(
            "DELETE",
            "/api/items/{id}",
            Arg.Any<int>(),
            Arg.Any<double>());
    }

    [Fact]
    public async Task InvokeAsync_EmptyPath_ShouldNormalizeToSlash()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Path = "";
        context.Request.Method = "GET";

        await middleware.InvokeAsync(context);

        _performanceMetrics.Received(1).RecordRequest(
            "GET",
            "/",
            Arg.Any<int>(),
            Arg.Any<double>());
    }
}
