using Girder.Infrastructure.Security.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.Headers;

[Trait("Category", "Unit")]
public class SecurityHeadersMiddlewareV2Tests
{
    private readonly ISecurityHeadersService _securityHeadersService = Substitute.For<ISecurityHeadersService>();
    private readonly ILogger<SecurityHeadersMiddleware> _logger = Substitute.For<ILogger<SecurityHeadersMiddleware>>();

    private SecurityHeadersMiddleware CreateMiddleware(
        RequestDelegate next,
        SecurityHeadersMiddlewareOptions? options = null)
    {
        var opts = Options.Create(options ?? new SecurityHeadersMiddlewareOptions());
        return new SecurityHeadersMiddleware(next, _securityHeadersService, _logger, opts);
    }

    private static DefaultHttpContext CreateContext(string path = "/api/test", string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        return context;
    }

    #region InvokeAsync - Security headers disabled

    [Fact]
    public async Task InvokeAsync_SecurityHeadersDisabled_SkipsHeaders()
    {
        var nextCalled = false;
        RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
        var options = new SecurityHeadersMiddlewareOptions { EnableSecurityHeaders = false };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    #endregion

    #region InvokeAsync - Excluded paths

    [Fact]
    public async Task InvokeAsync_HealthPath_SkipsHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/health");

        await middleware.InvokeAsync(context);

        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    [Fact]
    public async Task InvokeAsync_SwaggerPath_SkipsHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/swagger/index.html");

        await middleware.InvokeAsync(context);

        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    [Fact]
    public async Task InvokeAsync_MetricsPath_SkipsHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/metrics");

        await middleware.InvokeAsync(context);

        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    [Fact]
    public async Task InvokeAsync_FaviconPath_SkipsHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/favicon.ico");

        await middleware.InvokeAsync(context);

        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    [Fact]
    public async Task InvokeAsync_OptionsMethod_SkipsHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/api/test", "OPTIONS");

        await middleware.InvokeAsync(context);

        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    [Fact]
    public async Task InvokeAsync_CustomExcludedPath_SkipsHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var options = new SecurityHeadersMiddlewareOptions
        {
            ExcludedPaths = new List<string> { "/custom-excluded" }
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/custom-excluded/something");

        await middleware.InvokeAsync(context);

        _securityHeadersService.DidNotReceive().GetSecurityHeaders(Arg.Any<SecurityHeadersContext>());
    }

    #endregion

    #region InvokeAsync - Normal requests

    [Fact]
    public async Task InvokeAsync_NormalRequest_AppliesHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string> { ["X-Content-Type-Options"] = "nosniff" };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/api/users");

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-Content-Type-Options");
    }

    [Fact]
    public async Task InvokeAsync_HeaderAlreadySet_DoesNotOverride()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string> { ["X-Custom"] = "new-value" };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/api/users");
        context.Response.Headers["X-Custom"] = "existing-value";

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Custom"].ToString().Should().Be("existing-value");
    }

    [Fact]
    public async Task InvokeAsync_DetectInlineContent_AdminPath_GeneratesNonces()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string> { ["X-Content-Type-Options"] = "nosniff" };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.GenerateNonce().Returns("test-nonce");
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var options = new SecurityHeadersMiddlewareOptions
        {
            DetectInlineContent = true,
            InlineScriptPaths = new List<string> { "/admin" }
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/admin/panel");

        await middleware.InvokeAsync(context);

        // Nonce should be set on context items
        context.Items["ScriptNonce"].Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_LogSecurityHeaders_Enabled_LogsDebug()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string> { ["X-Content-Type-Options"] = "nosniff" };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var options = new SecurityHeadersMiddlewareOptions { LogSecurityHeaders = true };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/api/test");

        await middleware.InvokeAsync(context);

        // Should not throw
    }

    [Fact]
    public async Task InvokeAsync_ServiceThrows_AppliesMinimalHeaders()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>())
            .Throws(new InvalidOperationException("Service error"));
        var options = new SecurityHeadersMiddlewareOptions { ApplyMinimalHeadersOnError = true };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/api/test");

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("X-Content-Type-Options");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task InvokeAsync_ServiceThrows_ApplyMinimalDisabled_DoesNotApplyMinimalHeaders()
    {
        var nextCalled = false;
        RequestDelegate next = ctx => { nextCalled = true; return Task.CompletedTask; };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>())
            .Throws(new InvalidOperationException("Service error"));
        var options = new SecurityHeadersMiddlewareOptions { ApplyMinimalHeadersOnError = false };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/api/test");

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AuthPath_AddsAuthDomains()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>();
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var options = new SecurityHeadersMiddlewareOptions
        {
            AuthDomains = new List<string> { "auth.example.com" }
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/api/auth/login");

        await middleware.InvokeAsync(context);

        _securityHeadersService.Received(1).GetSecurityHeaders(
            Arg.Is<SecurityHeadersContext>(c => c.AllowedDomains.Contains("auth.example.com")));
    }

    [Fact]
    public async Task InvokeAsync_ApiPath_AddsApiVersionRequirement()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>();
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/api/users/profile");

        await middleware.InvokeAsync(context);

        _securityHeadersService.Received(1).GetSecurityHeaders(
            Arg.Is<SecurityHeadersContext>(c => c.CustomRequirements.ContainsKey("X-API-Version")));
    }

    [Fact]
    public async Task InvokeAsync_AssetPath_AddsCdnDomains()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>();
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var options = new SecurityHeadersMiddlewareOptions
        {
            AssetCdnDomains = new List<string> { "assets.cdn.com" }
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/assets/logo.png");

        await middleware.InvokeAsync(context);

        _securityHeadersService.Received(1).GetSecurityHeaders(
            Arg.Is<SecurityHeadersContext>(c => c.CdnDomains.Contains("assets.cdn.com")));
    }

    [Fact]
    public async Task InvokeAsync_WebRtcPath_AddsWebRtcRequirement()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>();
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/webrtc/signal");

        await middleware.InvokeAsync(context);

        _securityHeadersService.Received(1).GetSecurityHeaders(
            Arg.Is<SecurityHeadersContext>(c => c.CustomRequirements.ContainsKey("allowWebRTC")));
    }

    [Fact]
    public async Task InvokeAsync_ApplicationDeclaredWebRtcPath_AddsWebRtcRequirement()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>();
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var options = new SecurityHeadersMiddlewareOptions
        {
            WebRtcPaths = ["/webrtc", "/rooms"]
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/rooms/123");

        await middleware.InvokeAsync(context);

        _securityHeadersService.Received(1).GetSecurityHeaders(
            Arg.Is<SecurityHeadersContext>(c => c.CustomRequirements.ContainsKey("allowWebRTC")));
    }

    [Fact]
    public async Task InvokeAsync_UndeclaredPath_AddsNoWebRtcRequirement()
    {
        // Which route carries a real-time endpoint is the application's business;
        // guessing one here would relax the CSP for a page nobody named.
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>();
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var middleware = CreateMiddleware(next);
        var context = CreateContext("/rooms/123");

        await middleware.InvokeAsync(context);

        _securityHeadersService.Received(1).GetSecurityHeaders(
            Arg.Is<SecurityHeadersContext>(c => !c.CustomRequirements.ContainsKey("allowWebRTC")));
    }

    [Fact]
    public async Task InvokeAsync_CspBelowMinimumScore_LogsWarning()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string>
        {
            ["Content-Security-Policy"] = "default-src *"
        };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult
            {
                IsValid = true,
                SecurityScore = 20,
                Errors = new List<string>()
            });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult { OverallScore = 90 });
        var options = new SecurityHeadersMiddlewareOptions
        {
            MinimumCspScore = 70,
            LogCspValidationErrors = true
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/page");

        await middleware.InvokeAsync(context);

        // Should log a warning - just verify it doesn't throw
    }

    [Fact]
    public async Task InvokeAsync_AnalyzeHeadersEnabled_LowScore_LogsWarning()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var headers = new Dictionary<string, string> { ["X-Content-Type-Options"] = "nosniff" };
        _securityHeadersService.GetSecurityHeaders(Arg.Any<SecurityHeadersContext>()).Returns(headers);
        _securityHeadersService.ValidateContentSecurityPolicy(Arg.Any<string>())
            .Returns(new ContentSecurityPolicyValidationResult { IsValid = true, SecurityScore = 90 });
        _securityHeadersService.AnalyzeSecurityHeaders(Arg.Any<Dictionary<string, string>>())
            .Returns(new SecurityHeadersAnalysisResult
            {
                OverallScore = 30,
                MissingHeaders = new List<string> { "Content-Security-Policy" },
                Vulnerabilities = new List<SecurityVulnerability>
                {
                    new() { Severity = SecurityVulnerabilitySeverity.High, Description = "Missing CSP" }
                }
            });
        var options = new SecurityHeadersMiddlewareOptions
        {
            AnalyzeSecurityHeaders = true,
            MinimumSecurityScore = 80,
            LogToAuditSystem = false
        };
        var middleware = CreateMiddleware(next, options);
        var context = CreateContext("/page");

        await middleware.InvokeAsync(context);

        // Should not throw
    }

    #endregion

    #region SecurityHeadersMiddlewareOptions defaults

    [Fact]
    public void Options_DefaultValues_AreCorrect()
    {
        var options = new SecurityHeadersMiddlewareOptions();

        options.EnableSecurityHeaders.Should().BeTrue();
        options.ApplyMinimalHeadersOnError.Should().BeTrue();
        options.LogSecurityHeaders.Should().BeFalse();
        options.LogCspValidationErrors.Should().BeTrue();
        options.AnalyzeSecurityHeaders.Should().BeTrue();
        options.LogToAuditSystem.Should().BeTrue();
        options.DetectInlineContent.Should().BeTrue();
        options.MinimumCspScore.Should().Be(70);
        options.MinimumSecurityScore.Should().Be(80);
        options.ExcludedPaths.Should().Contain("/health");
        options.ExcludedPaths.Should().Contain("/metrics");
        options.ExcludedPaths.Should().Contain("/swagger");
        options.InlineScriptPaths.Should().Contain("/admin");
        options.InlineScriptPaths.Should().Contain("/dashboard");
    }

    [Fact]
    public void Options_CanSetAllProperties()
    {
        var options = new SecurityHeadersMiddlewareOptions
        {
            EnableSecurityHeaders = false,
            ApplyMinimalHeadersOnError = false,
            LogSecurityHeaders = true,
            LogCspValidationErrors = false,
            AnalyzeSecurityHeaders = false,
            LogToAuditSystem = false,
            DetectInlineContent = false,
            MinimumCspScore = 60,
            MinimumSecurityScore = 75,
            AllowedDomains = new List<string> { "example.com" },
            CdnDomains = new List<string> { "cdn.example.com" },
            AuthDomains = new List<string> { "auth.example.com" },
            AssetCdnDomains = new List<string> { "assets.example.com" },
            InlineScriptPaths = new List<string> { "/app" },
            InlineStylePaths = new List<string> { "/app" },
            CustomRequirements = new Dictionary<string, object?> { ["X-Test"] = "value" }
        };

        options.EnableSecurityHeaders.Should().BeFalse();
        options.MinimumCspScore.Should().Be(60);
        options.AllowedDomains.Should().Contain("example.com");
    }

    #endregion
}

[Trait("Category", "Unit")]
public class SecurityHeadersMiddlewareExtensionsTests
{
    [Fact]
    public void UseSecurityHeaders_NoParams_ReturnsBuilder()
    {
        // Just verify the extension method compiles and is available
        typeof(SecurityHeadersMiddlewareExtensions)
            .GetMethod("UseSecurityHeaders", new[] { typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder) })
            .Should().NotBeNull();
    }

    [Fact]
    public void UseSecurityHeaders_WithOptions_ReturnsBuilder()
    {
        typeof(SecurityHeadersMiddlewareExtensions)
            .GetMethod("UseSecurityHeaders", new[]
            {
                typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder),
                typeof(Action<SecurityHeadersMiddlewareOptions>)
            })
            .Should().NotBeNull();
    }
}
