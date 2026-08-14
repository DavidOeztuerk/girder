using System.Security.Claims;
using Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class TokenRevocationMiddlewareTests
{
    private readonly ITokenRevocationService _tokenRevocationService;
    private readonly ILogger<TokenRevocationMiddleware> _logger;

    public TokenRevocationMiddlewareTests()
    {
        _tokenRevocationService = Substitute.For<ITokenRevocationService>();
        _logger = Substitute.For<ILogger<TokenRevocationMiddleware>>();
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string? jti = "test-jti", string? userId = "user-123")
    {
        var claims = new List<Claim>();
        if (jti != null) claims.Add(new Claim("jti", jti));
        if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);

        var context = new DefaultHttpContext();
        context.User = principal;
        context.Request.Path = "/api/test";
        return context;
    }

    private static DefaultHttpContext CreateUnauthenticatedContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/test";
        return context;
    }

    [Fact]
    public async Task InvokeAsync_UnauthenticatedRequest_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenRevocationMiddleware(next, _tokenRevocationService, _logger);

        var context = CreateUnauthenticatedContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedRequest_NotRevoked_CallsNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenRevocationMiddleware(next, _tokenRevocationService, _logger);

        _tokenRevocationService.IsTokenRevokedAsync("test-jti", Arg.Any<CancellationToken>())
            .Returns(false);
        _tokenRevocationService.IsTokenRevokedAsync(Arg.Is<string>(s => s.Contains("pattern")), Arg.Any<CancellationToken>())
            .Returns(false);

        var context = CreateAuthenticatedContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_RevokedToken_Returns401()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenRevocationMiddleware(next, _tokenRevocationService, _logger);

        _tokenRevocationService.IsTokenRevokedAsync("test-jti", Arg.Any<CancellationToken>())
            .Returns(true);

        var context = CreateAuthenticatedContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_UserPatternRevoked_Returns401()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenRevocationMiddleware(next, _tokenRevocationService, _logger);

        _tokenRevocationService.IsTokenRevokedAsync("test-jti", Arg.Any<CancellationToken>())
            .Returns(false);
        _tokenRevocationService.IsTokenRevokedAsync(Arg.Is<string>(s => s.Contains("pattern")), Arg.Any<CancellationToken>())
            .Returns(true);

        var context = CreateAuthenticatedContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_NoJtiClaim_SkipsJtiCheck()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenRevocationMiddleware(next, _tokenRevocationService, _logger);

        _tokenRevocationService.IsTokenRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var context = CreateAuthenticatedContext(jti: null);
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ExceptionDuringCheck_ContinuesProcessing()
    {
        var nextCalled = false;
        RequestDelegate next = _ => { nextCalled = true; return Task.CompletedTask; };
        var middleware = new TokenRevocationMiddleware(next, _tokenRevocationService, _logger);

        _tokenRevocationService.IsTokenRevokedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Redis down"));

        var context = CreateAuthenticatedContext();
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
