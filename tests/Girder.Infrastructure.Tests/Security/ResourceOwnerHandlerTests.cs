using System.Security.Claims;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class ResourceOwnerHandlerTests
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ResourceOwnerHandler _handler;

    public ResourceOwnerHandlerTests()
    {
        _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var logger = Substitute.For<ILogger<ResourceOwnerHandler>>();
        _handler = new ResourceOwnerHandler(_httpContextAccessor, logger);
    }

    private AuthorizationHandlerContext CreateContext(
        string? userId,
        string? resourceIdParameter = "id",
        string? resourceIdValue = null,
        string[]? roles = null)
    {
        var claims = new List<Claim>();
        if (userId != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        if (roles != null)
        {
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);

        var requirement = new ResourceOwnerRequirement(resourceIdParameter ?? "id");

        var httpContext = new DefaultHttpContext();
        if (resourceIdValue != null)
        {
            httpContext.Request.RouteValues = new RouteValueDictionary
            {
                [resourceIdParameter ?? "id"] = resourceIdValue
            };
        }
        _httpContextAccessor.HttpContext.Returns(httpContext);

        return new AuthorizationHandlerContext(
            [requirement], principal, null);
    }

    [Fact]
    public async Task HandleRequirementAsync_OwnerAccessesOwnResource_Succeeds()
    {
        var context = CreateContext("user-123", resourceIdValue: "user-123");

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NonOwnerAccessesResource_Fails()
    {
        var context = CreateContext("user-123", resourceIdValue: "user-456");

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_AdminAccessesAnyResource_Succeeds()
    {
        var context = CreateContext("user-123", resourceIdValue: "user-456", roles: [Roles.Admin]);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_SuperAdminAccessesAnyResource_Succeeds()
    {
        var context = CreateContext("user-123", resourceIdValue: "user-456", roles: [Roles.SuperAdmin]);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoHttpContext_Fails()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "user-123") };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var requirement = new ResourceOwnerRequirement();

        var context = new AuthorizationHandlerContext([requirement], principal, null);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoUserIdClaim_Fails()
    {
        var context = CreateContext(null, resourceIdValue: "some-resource");

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoResourceIdInRoute_Fails()
    {
        var context = CreateContext("user-123", resourceIdValue: null);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_CustomResourceIdParameter_Works()
    {
        var context = CreateContext("user-123", resourceIdParameter: "userId", resourceIdValue: "user-123");

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }
}
