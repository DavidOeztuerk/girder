using Girder.Abstractions.Security;
using Girder.Infrastructure.Authorization;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Girder.Infrastructure.Tests.Authorization;

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
    public void ExactlyOneAttributeOfThisNameExists()
    {
        // There used to be two, in two namespaces, driving two different
        // mechanisms. Which one a call site got depended on its usings, and
        // picking the one whose mechanism was not wired left the endpoint open.
        var types = typeof(PermissionRequirement).Assembly
            .GetTypes()
            .Where(t => t.Name == nameof(RequirePermissionAttribute))
            .ToList();

        types.Should().ContainSingle();
        types[0].FullName.Should()
            .Be("Girder.Infrastructure.Security.Authorization.RequirePermissionAttribute");
    }

    [Fact]
    public void ItServesThePolicyPipeline()
    {
        var attr = new RequirePermissionAttribute("users:create");

        attr.Should().BeAssignableTo<AuthorizeAttribute>();
        attr.Policy.Should().Be("Permission:users:create");
    }

    [Fact]
    public void ItServesTheMiddleware()
    {
        // The middleware reads these two off the endpoint metadata.
        var attr = new RequirePermissionAttribute("users:read", "user-resource");

        attr.Permission.Should().Be("users:read");
        attr.Resource.Should().Be("user-resource");
    }

    [Fact]
    public void ResourceIsOptional()
    {
        new RequirePermissionAttribute("users:read").Resource.Should().BeNull();
    }

    [Fact]
    public void AnEmptyPermissionIsRefused()
    {
        // An attribute that names no permission protects nothing while looking
        // like it does.
        var act = () => new RequirePermissionAttribute(" ");

        act.Should().Throw<ArgumentException>();
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
    public async Task HandleAsync_UserHasGlobalWildcard_Succeeds()
    {
        var requirement = new PermissionRequirement("anything:action");
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "super-admin"),
            new Claim("permission", PermissionCatalog.Wildcard)
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
    public async Task AddGirderAuthorization_ThenPermissionAuthorization_ResolvesAnAttributePolicy()
    {
        // [RequirePermission] names a "Permission:" policy. Registering the role
        // policies alone leaves that name unanswered, and the framework then
        // rejects every request to the endpoint with "policy not found".
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGirderAuthorization();
        services.AddPermissionAuthorization();

        using var provider = services.BuildServiceProvider();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var attribute = new RequirePermissionAttribute("users:create");

        var policy = await policies.GetPolicyAsync(attribute.Policy!);

        policy.Should().NotBeNull();
        policy!.Requirements.Should().ContainSingle(r => r is PermissionRequirement);
    }
}

[Trait("Category", "Unit")]
public class TokenRevocationMiddlewareTests
{
    private readonly ITokenRevocationEvaluator _evaluator = Substitute.For<ITokenRevocationEvaluator>();
    private readonly ILogger<TokenRevocationMiddleware> _logger = Substitute.For<ILogger<TokenRevocationMiddleware>>();

    private TokenRevocationMiddleware CreateMiddleware(RequestDelegate? next = null) =>
        new(next ?? (_ => Task.CompletedTask), _evaluator, _logger);

    /// <summary>A signed-in caller whose token carries the claims the check needs.</summary>
    private static DefaultHttpContext SignedIn(
        string jti = "jti-1",
        string sub = "sub-1",
        long? issuedAt = null,
        string? sid = null)
    {
        var claims = new List<Claim>
        {
            new("jti", jti),
            new(ClaimTypes.NameIdentifier, sub),
            new("iat", (issuedAt ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds()).ToString())
        };
        if (sid is not null) claims.Add(new Claim("sid", sid));

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
    }

    private void Answers(RevocationVerdict verdict) =>
        _evaluator.EvaluateAsync(Arg.Any<TokenIdentity>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<RevocationVerdict>(verdict));

    [Fact]
    public async Task An_anonymous_request_is_not_checked()
    {
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(new DefaultHttpContext());

        nextCalled.Should().BeTrue();
        await _evaluator.DidNotReceive()
            .EvaluateAsync(Arg.Any<TokenIdentity>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_valid_token_passes_through()
    {
        Answers(RevocationVerdict.Valid);
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(SignedIn());

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_revoked_token_is_refused_with_401()
    {
        Answers(new RevocationVerdict(true, RevocationReason.TokenRevoked, false));
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = SignedIn();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
        nextCalled.Should().BeFalse("a revoked token must not reach the endpoint");
    }

    [Fact]
    public async Task The_claims_are_handed_to_the_evaluator_unchanged()
    {
        Answers(RevocationVerdict.Valid);
        var middleware = CreateMiddleware();
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();

        await middleware.InvokeAsync(SignedIn("jti-9", "sub-9", issuedAt, sid: "device-9"));

        await _evaluator.Received(1).EvaluateAsync(
            Arg.Is<TokenIdentity>(t =>
                t.TokenId == "jti-9"
                && t.SubjectId == "sub-9"
                && t.IssuedAt.ToUnixTimeSeconds() == issuedAt
                && t.SessionId == "device-9"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null, "sub-1", "1700000000")]
    [InlineData("jti-1", null, "1700000000")]
    [InlineData("jti-1", "sub-1", null)]
    [InlineData("jti-1", "sub-1", "not-a-number")]
    public async Task A_token_that_cannot_be_identified_is_refused(string? jti, string? sub, string? iat)
    {
        // Without jti, sub and iat there is nothing to look up. Letting such a
        // token through would make the unidentifiable one the only one that
        // never gets checked.
        var claims = new List<Claim>();
        if (jti is not null) claims.Add(new Claim("jti", jti));
        if (sub is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, sub));
        if (iat is not null) claims.Add(new Claim("iat", iat));

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task A_failing_evaluator_is_not_swallowed()
    {
        // Degradation is the evaluator's decision, not the middleware's. If it
        // reaches here, the request fails rather than quietly proceeding.
        _evaluator.EvaluateAsync(Arg.Any<TokenIdentity>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<RevocationVerdict>>(_ => throw new InvalidOperationException("store down"));
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        var act = () => middleware.InvokeAsync(SignedIn());

        await act.Should().ThrowAsync<InvalidOperationException>();
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public void UseTokenRevocation_registers_the_middleware_when_an_evaluator_exists()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_evaluator);
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UseTokenRevocation();

        act.Should().NotThrow();
    }

    [Fact]
    public void UseTokenRevocation_refuses_to_build_without_an_evaluator()
    {
        // Fail at composition, not at the first request — and never silently.
        var services = new ServiceCollection();
        services.AddLogging();
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UseTokenRevocation();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddNoTokenRevocation*");
    }
}
