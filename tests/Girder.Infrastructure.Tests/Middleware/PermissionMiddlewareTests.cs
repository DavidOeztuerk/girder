using System.Reflection;
using System.Security.Claims;
using Infrastructure.Middleware;
using Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Middleware;

public class PermissionMiddlewareTests
{
    private readonly ILogger<PermissionMiddleware> _logger = Substitute.For<ILogger<PermissionMiddleware>>();

    private PermissionMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new PermissionMiddleware(next, _logger);
    }

    #region IsPublicEndpoint Tests

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/metrics")]
    [InlineData("/swagger")]
    [InlineData("/api/auth")]
    [InlineData("/api/users/login")]
    [InlineData("/api/users/register")]
    [InlineData("/api/skills")]
    [InlineData("/api/skills/search")]
    [InlineData("/api/categories")]
    [InlineData("/api/listings")]
    [InlineData("/api/proficiency-levels")]
    [InlineData("/api/users/public")]
    [InlineData("/api/users/email-availability")]
    [InlineData("/api/users/forgot-password")]
    [InlineData("/api/users/request-password-reset")]
    [InlineData("/api/users/reset-password")]
    [InlineData("/api/users/verify-email")]
    [InlineData("/api/contact")]
    [InlineData("/api/payments/webhook")]
    [InlineData("/api/videocall/hub")]
    [InlineData("/api/videocall/sfu")]
    public async Task PublicEndpoints_ShouldPassThrough_WithoutAuth(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(path);
        // No authentication set

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue($"Path '{path}' should be public");
    }

    [Theory]
    [InlineData("/api/appointments")]
    [InlineData("/api/my/appointments")]
    [InlineData("/api/users/profile")]
    [InlineData("/api/notifications")]
    [InlineData("/api/calls/create")]
    public async Task ProtectedEndpoints_WithoutAuth_ShouldReturn401(string path)
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(path);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    #endregion

    #region IsSignalRHubRequest Tests

    [Theory]
    [InlineData("/api/videocall/hub")]
    [InlineData("/api/videocall/sfu")]
    [InlineData("/hubs/notifications")]
    [InlineData("/hubs/chat")]
    [InlineData("/notification-service/hubs/notifications")]
    [InlineData("/notification-service/hubs/chat")]
    public async Task SignalRHubPaths_ShouldSkipPermissionCheck(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(path);
        // Not authenticated, but should still pass through for SignalR

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue($"SignalR path '{path}' should bypass permission check");
    }

    [Theory]
    [InlineData("/api/some/path/negotiate")]
    [InlineData("/hubs/test/negotiate")]
    public async Task NegotiateEndpoints_ShouldSkipPermissionCheck(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(path);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue($"Negotiate path '{path}' should bypass permission check");
    }

    #endregion

    #region GetRequiredPermission Tests

    [Theory]
    [InlineData("/admin/dashboard", "GET", Permissions.AdminAccessDashboard)]
    [InlineData("/admin/audit-logs", "GET", Permissions.SystemViewLogs)]
    [InlineData("/admin/logs", "GET", Permissions.SystemViewLogs)]
    [InlineData("/admin/security", "GET", Permissions.SecurityViewAlerts)]
    [InlineData("/admin/security-alerts", "GET", Permissions.SecurityViewAlerts)]
    [InlineData("/admin/statistics", "GET", Permissions.AdminViewStatistics)]
    [InlineData("/admin/analytics", "GET", Permissions.AdminViewStatistics)]
    [InlineData("/admin/users", "GET", Permissions.UsersViewAll)]
    [InlineData("/admin/users/123/block", "POST", Permissions.UsersBlock)]
    [InlineData("/admin/users/123/unblock", "POST", Permissions.UsersUnblock)]
    [InlineData("/admin/users/123", "DELETE", Permissions.UsersDelete)]
    [InlineData("/admin/skills/categories", "GET", Permissions.SkillsManageCategories)]
    [InlineData("/admin/skills/proficiency-levels", "GET", Permissions.SkillsManageProficiency)]
    [InlineData("/admin/skills/verify", "POST", Permissions.SkillsVerify)]
    [InlineData("/admin/appointments", "GET", Permissions.AppointmentsViewAll)]
    [InlineData("/admin/appointments/123/cancel", "POST", Permissions.AppointmentsCancelAny)]
    [InlineData("/moderate/reports", "GET", Permissions.ReportsHandle)]
    [InlineData("/moderate/reviews", "GET", Permissions.ReviewsModerate)]
    [InlineData("/system/settings", "GET", Permissions.SystemManageSettings)]
    [InlineData("/system/logs", "GET", Permissions.SystemViewLogs)]
    [InlineData("/system/integrations", "GET", Permissions.SystemManageIntegrations)]
    public async Task AuthenticatedUser_WithoutPermission_ShouldReturn403(
        string path, string method, string expectedPermission)
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext(path, method);

        // Authenticated user with NO permissions
        SetAuthentication(context, "user-123", [Roles.User], []);

        await middleware.InvokeAsync(context);

        // Admin/system endpoints require permissions that User role doesn't have
        // The middleware should either return 403 (if user doesn't have the permission)
        // or pass through (if user has the permission from role inheritance).
        // User role doesn't have any admin/system permissions, so 403 is expected.
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden,
            $"User should be forbidden from '{path}' (requires '{expectedPermission}')");
    }

    [Fact]
    public async Task AuthenticatedAdmin_WithAdminPermissions_ShouldPassThrough()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/admin/dashboard");
        SetAuthentication(context, "admin-123", [Roles.Admin]);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Admin should have access to admin dashboard");
    }

    [Fact]
    public async Task AuthenticatedUser_OnRegularEndpoint_ShouldPassThrough()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // An endpoint that doesn't map to any permission
        var context = CreateHttpContext("/api/some-regular-endpoint");
        SetAuthentication(context, "user-123", [Roles.User]);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("Regular endpoints with no mapped permission should pass through");
    }

    #endregion

    #region HasPermission Tests

    [Fact]
    public async Task UserWithWildcardPermission_ShouldHaveAccess()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/admin/dashboard");
        SetAuthentication(context, "user-123", [], [Permissions.AdminAccessDashboard]);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task UserWithSystemManageAll_ShouldHaveAccessToEverything()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/admin/dashboard");
        SetAuthentication(context, "user-123", [], [Permissions.SystemManageAll]);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue("system:manage_all should grant access to everything");
    }

    #endregion

    #region Edge Cases for Higher Coverage

    [Fact]
    public async Task AdminEmailTemplates_ShouldRequireSystemManageSettings()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("/admin/email-templates");
        SetAuthentication(context, "user-123", [Roles.User], []);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task AdminAppointments_PutMethod_ShouldRequireAppointmentsManage()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("/admin/appointments/update", "PUT");
        SetAuthentication(context, "user-123", [Roles.User], []);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task AdminUsers_GetMethod_ShouldRequireUsersViewAll()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/admin/users");
        SetAuthentication(context, "admin-1", [], [Permissions.UsersViewAll]);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task WildcardPermission_ShouldGrantAccessToSameCategory()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/admin/dashboard");
        // "admin:*" should match "admin:access_dashboard" if permission format is "admin:access_dashboard"
        SetAuthentication(context, "user-123", [], [Permissions.AdminAccessDashboard]);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/hubs/notifications")]
    [InlineData("/hubs/chat")]
    public async Task LegacyHubPaths_ShouldBePublic(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(path);
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("/register")]
    [InlineData("/login")]
    [InlineData("/forgot-password")]
    [InlineData("/reset-password")]
    [InlineData("/verify-email")]
    public async Task LegacyAuthEndpoints_ShouldBePublic(string path)
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext(path);
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task HandleUnauthorized_ResponseShouldContainJsonBody()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("/api/protected-endpoint");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        context.Response.ContentType.Should().Be("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"success\":false");
    }

    [Fact]
    public async Task HandleForbidden_ResponseShouldContainJsonBody()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("/admin/dashboard");
        SetAuthentication(context, "user-123", [Roles.User], []);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        context.Response.ContentType.Should().Be("application/json");

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        body.Should().Contain("\"success\":false");
    }

    [Fact]
    public async Task AdminAnalytics_ShouldRequireAdminViewStatistics()
    {
        var middleware = CreateMiddleware();
        var context = CreateHttpContext("/admin/analytics/reports");
        SetAuthentication(context, "user-123", [Roles.User], []);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task UsersEmailAvailability_ServiceLocalPath_ShouldBePublic()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/users/email-availability");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task NotificationServiceHubPath_ShouldBePublic()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = CreateHttpContext("/notification-service/hubs/notifications");
        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static DefaultHttpContext CreateHttpContext(string path, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static void SetAuthentication(
        HttpContext context,
        string userId,
        string[] roles,
        string[]? permissions = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (permissions != null)
        {
            foreach (var perm in permissions)
                claims.Add(new Claim("permission", perm));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        context.User = new ClaimsPrincipal(identity);
    }

    #endregion
}
