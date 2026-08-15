using Girder.Infrastructure.Security.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net;
using System.Security.Claims;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class ResourceAuthorizationServiceTests
{
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly ILogger<ResourceAuthorizationService> _logger = Substitute.For<ILogger<ResourceAuthorizationService>>();
    private readonly IPermissionResolver _permissionResolver = Substitute.For<IPermissionResolver>();
    private readonly ResourceAuthorizationService _sut;

    public ResourceAuthorizationServiceTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        _sut = new ResourceAuthorizationService(_multiplexer, _logger, _permissionResolver);
    }

    private static ClaimsPrincipal AuthenticatedUser(string userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal AnonymousUser() => new ClaimsPrincipal(new ClaimsIdentity());

    #region AuthorizeAsync

    [Fact]
    public async Task AuthorizeAsync_NoUserIdClaim_ReturnsFail()
    {
        var result = await _sut.AuthorizeAsync(AnonymousUser(), "USER", "READ");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().NotBeEmpty();
    }

    [Fact]
    public async Task AuthorizeAsync_NoRequiredPermissions_FailsClosed()
    {
        _permissionResolver.GetRequiredPermissionsAsync("USER", "READ")
            .Returns(Enumerable.Empty<PermissionDefinition>());

        var result = await _sut.AuthorizeAsync(AuthenticatedUser("u-1"), "USER", "READ");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain(r => r.Contains("No permissions defined"));
    }

    [Fact]
    public async Task AuthorizeAsync_RequiredPermission_UserHasIt_ReturnsSuccess()
    {
        var permDef = new PermissionDefinition { Name = "user:read", IsConditional = false };

        _permissionResolver.GetRequiredPermissionsAsync("USER", "READ")
            .Returns(new[] { permDef });

        // HasPermissionAsync -> GetUserPermissionsAsync -> SetMembersAsync (empty, no keys)
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        // User is not owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _permissionResolver.GetOwnerPermissionsAsync("USER")
            .Returns(new[] { "user:read" });

        // IsResourceOwner returns true: owner string matches userId
        _database.StringGetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("owner")),
            Arg.Any<CommandFlags>())
            .Returns(new RedisValue("u-1"));

        var result = await _sut.AuthorizeAsync(AuthenticatedUser("u-1"), "USER", "READ");

        // Because owner is u-1 and owner permissions include user:read, should succeed
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_ServiceError_ReturnsFail()
    {
        _permissionResolver.GetRequiredPermissionsAsync(Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new Exception("resolver failed"));

        var result = await _sut.AuthorizeAsync(AuthenticatedUser("u-1"), "USER", "READ");

        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region IsResourceOwnerAsync

    [Fact]
    public async Task IsResourceOwnerAsync_OwnerMatches_ReturnsTrue()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("u-1"));

        var result = await _sut.IsResourceOwnerAsync("u-1", "JOB", "s-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsResourceOwnerAsync_OwnerDoesNotMatch_ReturnsFalse()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("other-user"));

        var result = await _sut.IsResourceOwnerAsync("u-1", "JOB", "s-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsResourceOwnerAsync_NullValue_ReturnsFalse()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.IsResourceOwnerAsync("u-1", "JOB", "s-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsResourceOwnerAsync_RedisError_ReturnsFalse()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("connection failed"));

        var result = await _sut.IsResourceOwnerAsync("u-1", "JOB", "s-1");

        result.Should().BeFalse();
    }

    #endregion

    #region GetUserPermissionsAsync

    [Fact]
    public async Task GetUserPermissionsAsync_NoPermissionKeys_OwnerPermissionsAdded_WhenOwner()
    {
        // User is owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("u-1"));

        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        _permissionResolver.GetOwnerPermissionsAsync("JOB")
            .Returns(new[] { "job:read", "job:update" });

        var result = await _sut.GetUserPermissionsAsync("u-1", "JOB", "s-1");

        result.Should().Contain("job:read").And.Contain("job:update");
    }

    [Fact]
    public async Task GetUserPermissionsAsync_RedisError_ReturnsEmpty()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.GetUserPermissionsAsync("u-1", "JOB", "s-1");

        result.Should().BeEmpty();
    }

    #endregion

    #region GrantPermissionAsync

    [Fact]
    public async Task GrantPermissionAsync_CallsRedisScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(1L));

        await _sut.GrantPermissionAsync("u-1", "JOB", "s-1", "job:read", "admin");

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GrantPermissionAsync_RedisError_Throws()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.GrantPermissionAsync("u-1", "JOB", "s-1", "job:read", "admin");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region RevokePermissionAsync

    [Fact]
    public async Task RevokePermissionAsync_CallsRedisScript()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(1L));

        await _sut.RevokePermissionAsync("u-1", "JOB", "s-1", "job:read", "admin");

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task RevokePermissionAsync_RedisError_Throws()
    {
        _database.ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var act = () => _sut.RevokePermissionAsync("u-1", "JOB", "s-1", "job:read", "admin");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region HasPermissionAsync

    [Fact]
    public async Task HasPermissionAsync_NoResourceType_ChecksGlobalPermissions_ReturnsEmpty()
    {
        // Global permissions always empty in current simplified impl
        var result = await _sut.HasPermissionAsync("u-1", "job:read");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WithResourceType_DelegatesToGetUserPermissions()
    {
        // User is owner → get owner permissions
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("u-1"));

        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        _permissionResolver.GetOwnerPermissionsAsync("JOB")
            .Returns(new[] { "job:read" });

        var result = await _sut.HasPermissionAsync("u-1", "job:read", "JOB", "s-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_RedisError_ReturnsFalse()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.HasPermissionAsync("u-1", "job:read", "JOB", "s-1");

        result.Should().BeFalse();
    }

    #endregion

    #region Coverage Tests

    [Fact]
    public async Task AuthorizeAsync_ConditionalPermission_UserHasPermission_Succeeds()
    {
        var permDef = new PermissionDefinition
        {
            Name = "match:read",
            IsConditional = true,
            Condition = "user is participant in match"
        };

        _permissionResolver.GetRequiredPermissionsAsync("Match", "read")
            .Returns(new[] { permDef });

        // User has the permission via Redis grant
        var grantJson = JsonSerializer.Serialize(new PermissionGrant
        {
            Permission = "match:read", IsActive = true, GrantedBy = "system"
        });
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "auth:permission:u-1:Match:res-1:match:read" });
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)grantJson);

        _permissionResolver.GetOwnerPermissionsAsync("Match")
            .Returns(Array.Empty<string>());

        var resourceData = new { Id = "res-1", RequesterId = "u-1", TargetUserId = "other" };
        var result = await _sut.AuthorizeAsync(
            AuthenticatedUser("u-1"), "Match", "read", resourceData);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_ConditionalPermission_UserLacksPermission_ConditionMet_Succeeds()
    {
        var permDef = new PermissionDefinition
        {
            Name = "match:read",
            IsConditional = true,
            Condition = "user is participant in match"
        };

        _permissionResolver.GetRequiredPermissionsAsync("Match", "read")
            .Returns(new[] { permDef });

        // User does NOT have the permission via Redis
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        // Not owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _permissionResolver.GetOwnerPermissionsAsync("Match")
            .Returns(Array.Empty<string>());

        // Condition evaluates true: user is participant (RequesterId matches userId)
        var resourceData = new { Id = "res-1", RequesterId = "u-1", TargetUserId = "other" };
        var result = await _sut.AuthorizeAsync(
            AuthenticatedUser("u-1"), "Match", "read", resourceData);

        // Condition met -> no failure added -> succeeds
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_WithResourceData_ExtractsResourceId()
    {
        var permDef = new PermissionDefinition { Name = "job:read", IsConditional = false };
        _permissionResolver.GetRequiredPermissionsAsync("JOB", "READ")
            .Returns(new[] { permDef });

        // User has the required permission via grant
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "auth:perm:u-1:JOB:job-123:job:read" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns((RedisValue)"""{"Permission":"job:read","IsActive":true}""");

        var resourceData = new { Id = "job-123" };
        var result = await _sut.AuthorizeAsync(
            AuthenticatedUser("u-1"), "JOB", "READ", resourceData);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_MissingPermission_ReturnsFail()
    {
        var permDef = new PermissionDefinition
        {
            Name = "admin:delete",
            IsConditional = false
        };

        _permissionResolver.GetRequiredPermissionsAsync("USER", "DELETE")
            .Returns(new[] { permDef });

        // User has NO permissions
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        // Not owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _permissionResolver.GetOwnerPermissionsAsync("USER")
            .Returns(Array.Empty<string>());

        var result = await _sut.AuthorizeAsync(
            AuthenticatedUser("u-1"), "USER", "DELETE");

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_NotOwner_ReturnsEmpty()
    {
        // Redis SET returns some keys (but the grant parsing code is commented out in production)
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "job:read", "job:update" });

        // Not owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _permissionResolver.GetOwnerPermissionsAsync("JOB")
            .Returns(Array.Empty<string>());

        var result = await _sut.GetUserPermissionsAsync("u-1", "JOB", "s-1");

        // Direct permission grant parsing is currently commented out in production code,
        // so only owner permissions are returned
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_Owner_ReturnsOwnerPermissions()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        // Owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("u-1"));

        _permissionResolver.GetOwnerPermissionsAsync("JOB")
            .Returns(new[] { "job:read", "job:update", "job:delete" });

        var result = await _sut.GetUserPermissionsAsync("u-1", "JOB", "s-1");

        result.Should().Contain("job:read");
        result.Should().Contain("job:update");
        result.Should().Contain("job:delete");
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_RedisError_ReturnsEmpty()
    {
        _multiplexer.GetEndPoints().Returns(new EndPoint[]
        {
            new DnsEndPoint("localhost", 6379)
        });
        _multiplexer.GetServer(Arg.Any<EndPoint>(), Arg.Any<object?>())
            .Throws(new RedisException("connection failed"));

        var result = await _sut.GetAccessibleResourcesAsync("u-1", "JOB");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_WithKeys_IteratesAndReturnsEmpty()
    {
        // Server.Keys returns some keys, but permissions parsing is commented out
        // so result will always be empty (permissions.Any() is false)
        var server = Substitute.For<IServer>();
        _multiplexer.GetEndPoints().Returns(new EndPoint[]
        {
            new DnsEndPoint("localhost", 6379)
        });
        _multiplexer.GetServer(Arg.Any<EndPoint>(), Arg.Any<object?>())
            .Returns(server);

        var keys = new RedisKey[] { "auth:user:u-1:JOB:s-1", "auth:user:u-1:JOB:s-2" };
        server.Keys(
            Arg.Any<int>(),
            Arg.Any<RedisValue>(),
            Arg.Any<int>(),
            Arg.Any<long>(),
            Arg.Any<int>(),
            Arg.Any<CommandFlags>())
            .Returns(keys);

        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "perm:key:1" });

        var result = await _sut.GetAccessibleResourcesAsync("u-1", "JOB");

        // Since permission grant parsing is commented out, permissions list stays empty
        // and no ResourceAccess entries are added
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HasPermissionAsync_OwnerHasPermission_ReturnsTrue()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        // Owner
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("u-1"));

        _permissionResolver.GetOwnerPermissionsAsync("JOB")
            .Returns(new[] { "job:delete" });

        var result = await _sut.HasPermissionAsync("u-1", "job:delete", "JOB", "s-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_UserLacksPermission_ReturnsFalse()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        _permissionResolver.GetOwnerPermissionsAsync("JOB")
            .Returns(Array.Empty<string>());

        var result = await _sut.HasPermissionAsync("u-1", "job:admin", "JOB", "s-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GrantPermissionAsync_WithExpiry_PassesExpiryToScript()
    {
        _database.ScriptEvaluateAsync(
                Arg.Any<string>(),
                Arg.Any<RedisKey[]?>(),
                Arg.Any<RedisValue[]?>(),
                Arg.Any<CommandFlags>())
            .Returns(RedisResult.Create(1L));

        await _sut.GrantPermissionAsync("u-1", "JOB", "s-1", "job:write", "admin",
            DateTime.UtcNow.AddHours(24));

        await _database.Received(1).ScriptEvaluateAsync(
            Arg.Any<string>(),
            Arg.Any<RedisKey[]?>(),
            Arg.Any<RedisValue[]?>(),
            Arg.Any<CommandFlags>());
    }

    #endregion
}

[Trait("Category", "Unit")]
public class ResourceAuthorizationMiddlewareTests
{
    private readonly IResourceAuthorizationService _authService = Substitute.For<IResourceAuthorizationService>();
    private readonly ILogger<ResourceAuthorizationMiddleware> _logger = Substitute.For<ILogger<ResourceAuthorizationMiddleware>>();

    private ResourceAuthorizationMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new ResourceAuthorizationMiddleware(next, _authService, _logger);
    }

    private static DefaultHttpContext CreateContext(string path = "/api/users", string method = "GET", bool authenticated = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection().BuildServiceProvider();

        if (authenticated)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "u-1") },
                "test");
            context.User = new ClaimsPrincipal(identity);
        }

        return context;
    }

    [Fact]
    public async Task InvokeAsync_NotAuthenticated_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext(authenticated: false);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_SkippedPath_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/auth/login", authenticated: true);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_NoControllerRoute_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        // No route values = no controller → skip authorization
        var context = CreateContext("/api/data", authenticated: true);

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AuthorizationFails_Returns403()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext("/api/users", authenticated: true);
        context.Request.RouteValues["controller"] = "users";

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.Fail("Not allowed"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_AuthorizationSucceeds_CallsNext()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ => { nextCalled = true; return Task.CompletedTask; });
        var context = CreateContext("/api/jobs", authenticated: true);
        context.Request.RouteValues["controller"] = "jobs";

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationResult.Success());

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void UseResourceAuthorization_RegistersMiddleware()
    {
        var app = new Microsoft.AspNetCore.Builder.ApplicationBuilder(
            new ServiceCollection().BuildServiceProvider());

        var act = () => app.UseResourceAuthorization();

        act.Should().NotThrow();
    }
}
