using Infrastructure.Authorization;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Infrastructure.Tests.Authorization;

[Trait("Category", "Unit")]
public class PermissionRequirementTests
{
    [Fact]
    public void PermissionRequirement_StoresPermission()
    {
        var req = new PermissionRequirement("users:read");

        req.Permission.Should().Be("users:read");
        req.Resource.Should().BeNull();
    }

    [Fact]
    public void PermissionRequirement_WithResource_StoresBoth()
    {
        var req = new PermissionRequirement("users:read", "USER");

        req.Permission.Should().Be("users:read");
        req.Resource.Should().Be("USER");
    }
}

[Trait("Category", "Unit")]
public class RequirePermissionAttributeTests
{
    [Fact]
    public void RequirePermissionAttribute_StoresPermissionAndSetsPolicy()
    {
        var attr = new RequirePermissionAttribute("users:create");

        attr.Permission.Should().Be("users:create");
        attr.Policy.Should().StartWith("Permission:");
        attr.Policy.Should().Contain("users:create");
    }
}

[Trait("Category", "Unit")]
public class PermissionPolicyProviderTests
{
    private PermissionPolicyProvider CreateProvider()
    {
        var authOptions = Options.Create(new AuthorizationOptions());
        return new PermissionPolicyProvider(authOptions);
    }

    [Fact]
    public async Task GetPolicyAsync_PermissionPrefix_ReturnsDynamicPolicy()
    {
        var provider = CreateProvider();

        var policy = await provider.GetPolicyAsync("Permission:users:read");

        policy.Should().NotBeNull();
        policy!.Requirements.Should().Contain(r => r is PermissionRequirement && ((PermissionRequirement)r).Permission == "users:read");
    }

    [Fact]
    public async Task GetPolicyAsync_UnknownPolicy_DelegatesToFallback()
    {
        var provider = CreateProvider();

        // Standard policies not registered = null from default provider
        var policy = await provider.GetPolicyAsync("SomeUnknownPolicy");

        // The fallback provider returns null for unknown policies
        policy.Should().BeNull();
    }

    [Fact]
    public async Task GetDefaultPolicyAsync_ReturnsPolicy()
    {
        var provider = CreateProvider();

        var policy = await provider.GetDefaultPolicyAsync();

        policy.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFallbackPolicyAsync_ReturnsNullByDefault()
    {
        var provider = CreateProvider();

        var policy = await provider.GetFallbackPolicyAsync();

        // Default fallback policy is null unless configured
        policy.Should().BeNull();
    }
}

[Trait("Category", "Unit")]
public class PermissionAuthorizationHandlerTests
{
    private readonly ILogger<PermissionAuthorizationHandler> _logger = Substitute.For<ILogger<PermissionAuthorizationHandler>>();
    private readonly PermissionAuthorizationHandler _sut;

    public PermissionAuthorizationHandlerTests()
    {
        _sut = new PermissionAuthorizationHandler(_logger);
    }

    private static ClaimsPrincipal UserWithPermission(string permission, string? role = null)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, "u-1"),
            new Claim("permission", permission)
        };
        if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal AnonymousUser() => new ClaimsPrincipal(new ClaimsIdentity());

    [Fact]
    public async Task HandleAsync_UserHasPermission_Succeeds()
    {
        var requirement = new PermissionRequirement("users:read");
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            UserWithPermission("users:read"),
            null);

        await _sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_UserLacksPermission_DoesNotSucceed()
    {
        var requirement = new PermissionRequirement("users:delete");
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            UserWithPermission("users:read"),
            null);

        await _sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_UserHasWildcard_Succeeds()
    {
        var requirement = new PermissionRequirement("users:create");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "u-1"),
            new Claim("permission", "users:*")
        }, "test"));

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, null);

        await _sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NotAuthenticated_DoesNotSucceed()
    {
        var requirement = new PermissionRequirement("users:read");
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            AnonymousUser(),
            null);

        await _sut.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_UserHasSystemManageAll_Succeeds()
    {
        var requirement = new PermissionRequirement("anything:action");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "super-admin"),
            new Claim("permission", Permissions.SystemManageAll)
        }, "test"));

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, null);

        await _sut.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }
}

[Trait("Category", "Unit")]
public class PermissionAuthorizationExtensionsTests
{
    [Fact]
    public void AddPermissionAuthorization_RegistersRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPermissionAuthorization();

        services.Should().Contain(sd => sd.ServiceType == typeof(IAuthorizationPolicyProvider));
        services.Should().Contain(sd => sd.ServiceType == typeof(IAuthorizationHandler));
    }

    [Fact]
    public void AddPermissionPolicies_AddsKnownPolicies()
    {
        var authOptions = new AuthorizationOptions();

        authOptions.AddPermissionPolicies();

        authOptions.GetPolicy("AdminOnly").Should().NotBeNull();
        authOptions.GetPolicy("ModeratorOnly").Should().NotBeNull();
        authOptions.GetPolicy("SuperAdminOnly").Should().NotBeNull();
        authOptions.GetPolicy("CanManageUsers").Should().NotBeNull();
        authOptions.GetPolicy("CanViewAllUsers").Should().NotBeNull();
        authOptions.GetPolicy("CanManageSkills").Should().NotBeNull();
        authOptions.GetPolicy("CanModerateContent").Should().NotBeNull();
        authOptions.GetPolicy("CanAccessAdmin").Should().NotBeNull();
    }
}

[Trait("Category", "Unit")]
public class TokenRevocationMiddlewareTests
{
    private readonly ITokenRevocationService _revocationService = Substitute.For<ITokenRevocationService>();
    private readonly ILogger<TokenRevocationMiddleware> _logger = Substitute.For<ILogger<TokenRevocationMiddleware>>();

    private TokenRevocationMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new TokenRevocationMiddleware(next, _revocationService, _logger);
    }

    private static DefaultHttpContext CreateContext(bool authenticated = false, string? jti = null, string? userId = null)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (authenticated)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId ?? "u-1")
            };
            if (jti != null) claims.Add(new Claim("jti", jti));

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        return context;
    }

    [Fact]
    public async Task InvokeAsync_NotAuthenticated_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(CreateContext(authenticated: false));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_TokenNotRevoked_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });

        _revocationService.IsTokenRevokedAsync(Arg.Any<string>()).Returns(false);

        await middleware.InvokeAsync(CreateContext(authenticated: true, jti: "jti-1"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_TokenRevoked_Returns401()
    {
        var middleware = CreateMiddleware();

        _revocationService.IsTokenRevokedAsync("jti-revoked").Returns(true);
        _revocationService.IsTokenRevokedAsync(Arg.Is<string>(s => s != "jti-revoked")).Returns(false);

        var context = CreateContext(authenticated: true, jti: "jti-revoked");

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_RevocationServiceThrows_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });

        _revocationService.IsTokenRevokedAsync(Arg.Any<string>())
            .ThrowsAsync(new Exception("redis down"));

        await middleware.InvokeAsync(CreateContext(authenticated: true, jti: "jti-1"));

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void UseTokenRevocation_RegistersMiddleware()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_revocationService);
        services.AddLogging();

        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UseTokenRevocation();

        act.Should().NotThrow();
    }
}
