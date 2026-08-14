using Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class SecurityHeadersMiddlewareTests
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();

    public SecurityHeadersMiddlewareTests()
    {
        var configData = new Dictionary<string, string?>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private SecurityHeadersMiddleware CreateMiddleware(
        RequestDelegate? next = null,
        IConfiguration? config = null,
        IHostEnvironment? env = null)
    {
        next ??= _ => Task.CompletedTask;
        return new SecurityHeadersMiddleware(next, config ?? _configuration, env ?? _environment);
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
    public async Task InvokeAsync_ShouldSetXFrameOptions()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetXXssProtection()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-XSS-Protection"].ToString().Should().Be("1; mode=block");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetXContentTypeOptions()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetReferrerPolicy()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetContentSecurityPolicy()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("default-src 'self'");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetPermissionsPolicy()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var pp = context.Response.Headers["Permissions-Policy"].ToString();
        pp.Should().Contain("camera=(self)");
        pp.Should().Contain("microphone=(self)");
        pp.Should().Contain("geolocation=()");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetCrossOriginPolicies()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["Cross-Origin-Opener-Policy"].ToString().Should().Be("same-origin-allow-popups");
        context.Response.Headers["Cross-Origin-Resource-Policy"].ToString().Should().Be("same-origin");
        context.Response.Headers["Cross-Origin-Embedder-Policy"].ToString().Should().Be("credentialless");
    }

    [Fact]
    public async Task InvokeAsync_ShouldSetXPermittedCrossDomainPolicies()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["X-Permitted-Cross-Domain-Policies"].ToString().Should().Be("none");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRemoveServerHeader()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Response.Headers["Server"] = "Kestrel";

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Server");
    }

    [Fact]
    public async Task InvokeAsync_ShouldRemoveXPoweredByHeader()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Response.Headers["X-Powered-By"] = "ASP.NET";

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("X-Powered-By");
    }

    [Fact]
    public async Task InvokeAsync_Production_ShouldSetHsts()
    {
        _environment.EnvironmentName.Returns("Production");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().Contain("max-age=31536000");
    }

    [Fact]
    public async Task InvokeAsync_Development_ShouldNotSetHsts()
    {
        _environment.EnvironmentName.Returns("Development");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task InvokeAsync_Production_ShouldSetCspReportOnly()
    {
        _environment.EnvironmentName.Returns("Production");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("Content-Security-Policy-Report-Only");
    }

    [Fact]
    public async Task InvokeAsync_Development_ShouldNotSetCspReportOnly()
    {
        _environment.EnvironmentName.Returns("Development");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Content-Security-Policy-Report-Only");
    }

    [Fact]
    public async Task InvokeAsync_Production_ShouldSetExpectCt()
    {
        _environment.EnvironmentName.Returns("Production");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().ContainKey("Expect-CT");
    }

    [Fact]
    public async Task InvokeAsync_Development_ShouldNotSetExpectCt()
    {
        _environment.EnvironmentName.Returns("Development");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Response.Headers.Should().NotContainKey("Expect-CT");
    }

    [Fact]
    public async Task InvokeAsync_ShouldStoreCspNonceInHttpContextItems()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        context.Items["CspNonce"].Should().NotBeNull();
        context.Items["CspNonce"].Should().BeOfType<string>();
        ((string)context.Items["CspNonce"]!).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvokeAsync_Development_CspShouldContainUnsafeInlineForScripts()
    {
        _environment.EnvironmentName.Returns("Development");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("'unsafe-inline'");
        csp.Should().Contain("'unsafe-eval'");
    }

    [Fact]
    public async Task InvokeAsync_Production_CspShouldUseNonceForScripts()
    {
        _environment.EnvironmentName.Returns("Production");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("'nonce-");
        csp.Should().NotContain("'unsafe-eval'");
    }

    [Fact]
    public async Task InvokeAsync_WithSentryDsn_ShouldIncludeSentryInConnectSrc()
    {
        var configData = new Dictionary<string, string?>
        {
            ["Sentry:Dsn"] = "https://key@o123456.ingest.sentry.io/123"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        var middleware = CreateMiddleware(config: config);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("o123456.ingest.sentry.io");
    }

    [Fact]
    public async Task InvokeAsync_Production_CspShouldContainUpgradeInsecureRequests()
    {
        _environment.EnvironmentName.Returns("Production");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().Contain("upgrade-insecure-requests");
    }

    [Fact]
    public async Task InvokeAsync_Development_CspShouldNotContainUpgradeInsecureRequests()
    {
        _environment.EnvironmentName.Returns("Development");
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var csp = context.Response.Headers["Content-Security-Policy"].ToString();
        csp.Should().NotContain("upgrade-insecure-requests");
    }
}
