using Girder.Infrastructure.Security.Audit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.Audit;

[Trait("Category", "Unit")]
public class SecurityAuditMiddlewareV2Tests
{
    private readonly ISecurityAuditService _auditService = Substitute.For<ISecurityAuditService>();
    private readonly ILogger<SecurityAuditMiddleware> _logger = Substitute.For<ILogger<SecurityAuditMiddleware>>();

    private SecurityAuditMiddleware CreateMiddleware(RequestDelegate next)
    {
        return new SecurityAuditMiddleware(next, _auditService, _logger);
    }

    private static DefaultHttpContext CreateContext(
        string method = "GET",
        string path = "/api/test",
        int statusCode = 200)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.StatusCode = statusCode;
        return context;
    }

    #region InvokeAsync - should log

    [Fact]
    public async Task InvokeAsync_PostRequest_LogsAuditEvent()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/users");

        await middleware.InvokeAsync(context);

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_PutRequest_LogsAuditEvent()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("PUT", "/api/users/1");

        await middleware.InvokeAsync(context);

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_DeleteRequest_LogsAuditEvent()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("DELETE", "/api/users/1");

        await middleware.InvokeAsync(context);

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_LoginPath_LogsAuditEvent()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/api/auth/login");

        await middleware.InvokeAsync(context);

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AdminPath_LogsAuditEvent()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/admin/users");

        await middleware.InvokeAsync(context);

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_400StatusCode_LogsAuditEvent()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 400;
            return Task.CompletedTask;
        };
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/api/test");

        await middleware.InvokeAsync(context);

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_500StatusCode_LogsHighSeverity()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 500;
            return Task.CompletedTask;
        };
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/api/test");
        context.Response.StatusCode = 500;

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_GetToRegularPath_DoesNotLog()
    {
        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/api/products", 200);

        await middleware.InvokeAsync(context);

        await _auditService.DidNotReceive().LogSecurityEventAsync(
            Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region InvokeAsync - exception handling

    [Fact]
    public async Task InvokeAsync_NextThrows_LogsExceptionAndRethrows()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => throw new InvalidOperationException("Service error");
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/api/test");

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>();

        await _auditService.Received(1).LogSecurityEventAsync(
            Arg.Is<SecurityAuditEvent>(e => e.Result == "Failure"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_AuditServiceThrows_DoesNotPropagateException()
    {
        _auditService.LogSecurityEventAsync(Arg.Any<SecurityAuditEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Audit failure"));

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/users");

        // Should not throw - audit failures are swallowed
        var act = () => middleware.InvokeAsync(context);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region InvokeAsync - event content

    [Fact]
    public async Task InvokeAsync_LoginPath_EventTypeIsUserLogin()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/auth/login");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("UserLogin");
    }

    [Fact]
    public async Task InvokeAsync_LogoutPath_EventTypeIsUserLogout()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/auth/logout");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("UserLogout");
    }

    [Fact]
    public async Task InvokeAsync_DeleteMethod_EventTypeIsDataDeletion()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("DELETE", "/api/jobs/1");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("DataDeletion");
    }

    [Fact]
    public async Task InvokeAsync_PostMethod_EventTypeIsDataModification()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/jobs");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("DataModification");
    }

    [Fact]
    public async Task InvokeAsync_AdminPath_EventTypeIsAdminAction()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/admin/users/list");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.EventType.Should().Be("AdminAction");
    }

    [Fact]
    public async Task InvokeAsync_WithForwardedIp_UsesForwardedIp()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/auth/login");
        context.Request.Headers["X-Forwarded-For"] = "10.0.0.1, 10.0.0.2";

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.IpAddress.Should().Be("10.0.0.1");
    }

    [Fact]
    public async Task InvokeAsync_WithRealIp_UsesRealIp()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/auth/login");
        context.Request.Headers["X-Real-IP"] = "192.168.0.1";

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.IpAddress.Should().Be("192.168.0.1");
    }

    [Fact]
    public async Task InvokeAsync_UserPaymentPath_HasSoxComplianceFlag()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/payment/process");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.ComplianceFlags.Should().Contain("SOX");
    }

    [Fact]
    public async Task InvokeAsync_UserPath_HasGdprComplianceFlag()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/user/profile");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.ComplianceFlags.Should().Contain("GDPR");
    }

    [Fact]
    public async Task InvokeAsync_GetMethod_ActionIsRead()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx =>
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        var middleware = CreateMiddleware(next);
        var context = CreateContext("GET", "/api/protected");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.Action.Should().Be("Read");
    }

    [Fact]
    public async Task InvokeAsync_SensitivePath_TaggedAsSensitive()
    {
        SecurityAuditEvent? capturedEvent = null;
        _auditService.LogSecurityEventAsync(Arg.Do<SecurityAuditEvent>(e => capturedEvent = e), Arg.Any<CancellationToken>())
            .Returns("event-id");

        RequestDelegate next = ctx => Task.CompletedTask;
        var middleware = CreateMiddleware(next);
        var context = CreateContext("POST", "/api/user/password");

        await middleware.InvokeAsync(context);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.Tags.Should().Contain("sensitive");
    }

    #endregion
}

[Trait("Category", "Unit")]
public class SecurityAuditMiddlewareExtensionsV2Tests
{
    [Fact]
    public void UseSecurityAudit_ExtensionMethodExists()
    {
        typeof(SecurityAuditMiddlewareExtensions)
            .GetMethod("UseSecurityAudit", new[]
            {
                typeof(Microsoft.AspNetCore.Builder.IApplicationBuilder)
            })
            .Should().NotBeNull();
    }
}

[Trait("Category", "Unit")]
public class SecurityAuditExtensionsTests
{
    [Fact]
    public void AddSecurityAuditMiddleware_RegistersMiddleware()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddSecurityAuditMiddleware();

        services.Any(sd => sd.ServiceType == typeof(SecurityAuditMiddleware)).Should().BeTrue();
    }
}
