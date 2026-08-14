using Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InputSanitizationMiddlewareTests
{
    private readonly IInputSanitizer _sanitizer = Substitute.For<IInputSanitizer>();
    private readonly ILogger<InputSanitizationMiddleware> _logger = Substitute.For<ILogger<InputSanitizationMiddleware>>();

    private InputSanitizationMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        InputSanitizationOptions? options = null)
    {
        var opts = Options.Create(options ?? new InputSanitizationOptions());
        next ??= _ => Task.CompletedTask;
        return new InputSanitizationMiddleware(next, _sanitizer, _logger, opts);
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        ctx.Response.Body = new MemoryStream();
        ctx.RequestServices = new ServiceCollection().BuildServiceProvider();
        return ctx;
    }

    #region ShouldSkip paths

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/metrics")]
    [InlineData("/swagger/index.html")]
    [InlineData("/auth/register")]
    [InlineData("/auth/login")]
    [InlineData("/hub")]
    [InlineData("/hubs/chat")]
    [InlineData("/webhook")]
    [InlineData("/payments/webhook")]
    [InlineData("/favicon.ico")]
    public async Task InvokeAsync_SkippedPath_DoesNotSanitize(string path)
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        _sanitizer.DidNotReceive().DetectInjectionAttempt(Arg.Any<string>());
    }

    [Theory]
    [InlineData("/app.css")]
    [InlineData("/bundle.js")]
    [InlineData("/logo.png")]
    [InlineData("/bg.jpg")]
    [InlineData("/anim.gif")]
    public async Task InvokeAsync_StaticFile_DoesNotSanitize(string path)
    {
        var middleware = CreateMiddleware();
        var context = CreateContext(path);

        await middleware.InvokeAsync(context);

        _sanitizer.DidNotReceive().DetectInjectionAttempt(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_OptionsMethod_DoesNotSanitize()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/test", "OPTIONS");

        await middleware.InvokeAsync(context);

        _sanitizer.DidNotReceive().DetectInjectionAttempt(Arg.Any<string>());
    }

    [Fact]
    public async Task InvokeAsync_Disabled_DoesNotSanitize()
    {
        var options = new InputSanitizationOptions { EnableInputSanitization = false };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/api/data");

        await middleware.InvokeAsync(context);

        _sanitizer.DidNotReceive().DetectInjectionAttempt(Arg.Any<string>());
    }

    #endregion

    #region Normal pass-through

    [Fact]
    public async Task InvokeAsync_NoQueryParams_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/data");

        _sanitizer.SanitizeText(Arg.Any<string>(), Arg.Any<TextSanitizationOptions>())
            .Returns(x => x.ArgAt<string>(0));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SafeQueryParam_SanitizesAndCallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/data");
        context.Request.QueryString = new QueryString("?name=John");

        _sanitizer.DetectInjectionAttempt("John").Returns(new InjectionDetectionResult { InjectionDetected = false });
        _sanitizer.SanitizeText("John", Arg.Any<TextSanitizationOptions>()).Returns("John");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    #endregion

    #region Injection blocking

    [Fact]
    public async Task InvokeAsync_InjectionInQuery_BlockOnDetection_Returns400()
    {
        var middleware = CreateMiddleware(options: new InputSanitizationOptions { BlockOnInjectionDetection = true });
        var context = CreateContext("/api/data");
        context.Request.QueryString = new QueryString("?input=<script>");

        _sanitizer.DetectInjectionAttempt("<script>")
            .Returns(new InjectionDetectionResult
            {
                InjectionDetected = true,
                InjectionType = InjectionType.XssInjection,
                DetectedPatterns = new List<string> { "xss" }
            });
        _sanitizer.SanitizeText(Arg.Any<string>(), Arg.Any<TextSanitizationOptions>())
            .Returns(x => x.ArgAt<string>(0));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_InjectionDetected_BlockOff_CallsNext()
    {
        var nextCalled = false;
        var options = new InputSanitizationOptions { BlockOnInjectionDetection = false };
        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            options: options);
        var context = CreateContext("/api/data");
        context.Request.QueryString = new QueryString("?input=<script>");

        _sanitizer.DetectInjectionAttempt("<script>")
            .Returns(new InjectionDetectionResult { InjectionDetected = true, DetectedPatterns = new List<string>() });
        _sanitizer.SanitizeText(Arg.Any<string>(), Arg.Any<TextSanitizationOptions>())
            .Returns("sanitized");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    #endregion

    #region Sanitization error handling

    [Fact]
    public async Task InvokeAsync_SanitizerThrows_BlockOnError_Returns400()
    {
        var options = new InputSanitizationOptions { BlockOnSanitizationError = true };
        var middleware = CreateMiddleware(options: options);
        var context = CreateContext("/api/data");
        context.Request.QueryString = new QueryString("?x=y");

        _sanitizer.DetectInjectionAttempt(Arg.Any<string>())
            .Throws(new Exception("sanitizer crashed"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task InvokeAsync_SanitizerThrows_BlockOff_CallsNext()
    {
        var nextCalled = false;
        var options = new InputSanitizationOptions { BlockOnSanitizationError = false };
        var middleware = CreateMiddleware(
            next: _ => { nextCalled = true; return Task.CompletedTask; },
            options: options);
        var context = CreateContext("/api/data");
        context.Request.QueryString = new QueryString("?x=y");

        _sanitizer.DetectInjectionAttempt(Arg.Any<string>())
            .Throws(new Exception("sanitizer crashed"));

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    #endregion

    #region UseInputSanitization extension

    [Fact]
    public void UseInputSanitization_RegistersMiddleware()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(services);

        var act = () => app.UseInputSanitization();

        act.Should().NotThrow();
    }

    #endregion
}
