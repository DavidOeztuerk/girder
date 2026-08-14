using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Json;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Security.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class PermissionMiddlewareTests
{
    private static readonly IPermissionCatalog Catalog = new PermissionCatalogBuilder()
        .Role("User", "profile:read_own")
        .Role("Admin", "users:view_all", "users:delete")
        .RoleInherits("Admin", "User")
        .Build();

    private static readonly IEndpointAccessPolicy Policy = new EndpointAccessPolicyBuilder()
        .Public("/health", "/swagger")
        .Require("users:view_all", "/admin/users", "GET")
        .Require("users:delete", "/admin/users", "DELETE")
        .Require("reports:read", "/admin/reports")
        .Build();

    private static PermissionMiddleware Middleware(
        RequestDelegate? next = null,
        IPermissionCatalog? catalog = null,
        IEndpointAccessPolicy? policy = null) =>
        new(next ?? (_ => Task.CompletedTask),
            NullLogger<PermissionMiddleware>.Instance,
            catalog ?? Catalog,
            policy ?? Policy);

    private static DefaultHttpContext Request(
        string path,
        string method = "GET",
        ClaimsPrincipal? user = null,
        Endpoint? endpoint = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();

        if (user is not null)
        {
            context.User = user;
        }

        if (endpoint is not null)
        {
            context.SetEndpoint(endpoint);
        }

        return context;
    }

    private static ClaimsPrincipal Caller(string[]? permissions = null, string[]? roles = null)
    {
        var claims = new List<Claim>();
        foreach (var permission in permissions ?? [])
        {
            claims.Add(new Claim("permission", permission));
        }

        foreach (var role in roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    private static async Task<string> BodyOf(HttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    #region Public and anonymous

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/swagger")]
    public async Task PathMarkedPublic_PassesWithoutAuthentication(string path)
    {
        var called = false;
        var context = Request(path);

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task UnlistedPath_WithoutAuthentication_Returns401()
    {
        var context = Request("/admin/users");

        await Middleware().InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task AllowAnonymousMetadata_PassesWithoutAuthentication()
    {
        var called = false;
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new AllowAnonymousAttribute()),
            "anonymous");
        var context = Request("/admin/users", endpoint: endpoint);

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    #endregion

    #region Realtime handshakes

    [Theory]
    [InlineData("/anything/negotiate")]
    [InlineData("/hubs/chat/negotiate")]
    public async Task NegotiateRequest_SkipsTheCheck(string path)
    {
        var called = false;
        var context = Request(path);

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task WebSocketUpgrade_SkipsTheCheck()
    {
        var called = false;
        var context = Request("/admin/users");
        context.Features.Set<IHttpWebSocketFeature>(new FakeWebSocketFeature());

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    #endregion

    #region Permission evaluation

    [Fact]
    public async Task AuthenticatedWithoutTheRequiredPermission_Returns403()
    {
        var context = Request("/admin/users", user: Caller(permissions: ["profile:read_own"]));

        await Middleware().InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task ExplicitPermissionClaim_PassesThrough()
    {
        var called = false;
        var context = Request("/admin/users", user: Caller(permissions: ["users:view_all"]));

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionGrantedByRole_PassesThrough()
    {
        var called = false;
        var context = Request("/admin/users", user: Caller(roles: ["Admin"]));

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task PermissionGrantedByAnInheritedRole_PassesThrough()
    {
        var called = false;
        var policy = new EndpointAccessPolicyBuilder()
            .Require("profile:read_own", "/me")
            .Build();
        var context = Request("/me", user: Caller(roles: ["Admin"]));

        await Middleware(_ => { called = true; return Task.CompletedTask; }, policy: policy)
            .InvokeAsync(context);

        called.Should().BeTrue("Admin inherits User, which grants profile:read_own");
    }

    [Fact]
    public async Task CategoryWildcard_GrantsEverythingInThatCategory()
    {
        var called = false;
        var context = Request("/admin/users", user: Caller(permissions: ["users:*"]));

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task GlobalWildcard_GrantsEverything()
    {
        var called = false;
        var context = Request("/admin/users", user: Caller(permissions: [PermissionCatalog.Wildcard]));

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task CategoryWildcard_DoesNotGrantAnotherCategory()
    {
        var context = Request("/admin/users", user: Caller(permissions: ["reports:*"]));

        await Middleware().InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    #endregion

    #region Rule selection

    [Fact]
    public async Task MethodScopedRule_AppliesOnlyToThatMethod()
    {
        // DELETE /admin/users needs users:delete, which this caller lacks.
        var deleteContext = Request("/admin/users", "DELETE", Caller(permissions: ["users:view_all"]));
        await Middleware().InvokeAsync(deleteContext);
        deleteContext.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

        // GET is a separate rule the same caller does satisfy.
        var called = false;
        var getContext = Request("/admin/users", "GET", Caller(permissions: ["users:view_all"]));
        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(getContext);
        called.Should().BeTrue();
    }

    [Fact]
    public async Task RuleWithoutMethods_AppliesToEveryMethod()
    {
        foreach (var method in new[] { "GET", "POST", "DELETE" })
        {
            var context = Request("/admin/reports", method, Caller(permissions: ["something:else"]));

            await Middleware().InvokeAsync(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }
    }

    [Fact]
    public async Task EndpointAttribute_TakesPrecedenceOverThePolicy()
    {
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new RequirePermissionAttribute("reports:read")),
            "attributed");

        // The policy would ask for users:view_all here; the attribute wins.
        var context = Request("/admin/users", user: Caller(permissions: ["users:view_all"]), endpoint: endpoint);

        await Middleware().InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task WithoutPolicyOrAttribute_NoPermissionIsRequired()
    {
        var called = false;
        var context = Request("/whatever", user: Caller());

        await Middleware(
                _ => { called = true; return Task.CompletedTask; },
                policy: EndpointAccessPolicy.Empty)
            .InvokeAsync(context);

        called.Should().BeTrue();
    }

    #endregion

    #region Responses

    [Fact]
    public async Task UnauthorizedResponse_IsJson()
    {
        var context = Request("/admin/users");

        await Middleware().InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await BodyOf(context));
        body.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("errors").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ForbiddenResponse_NamesTheMissingPermission()
    {
        var context = Request("/admin/users", user: Caller(permissions: ["profile:read_own"]));

        await Middleware().InvokeAsync(context);

        context.Response.ContentType.Should().Be("application/json");
        using var body = JsonDocument.Parse(await BodyOf(context));
        body.RootElement.GetProperty("message").GetString().Should().Contain("users:view_all");
    }

    #endregion

    private sealed class FakeWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            throw new NotSupportedException();
    }
}
