using Girder.InMemory.Security;
using Girder.Redis.Security.Authorization;
using Girder.Abstractions.Security.Authorization;
using System.Security.Claims;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class AuthorizationExtensionsTests
{
    #region ResourceRequirement

    [Fact]
    public void ResourceRequirement_ActionOnly_SetsActionAndNullResourceType()
    {
        var requirement = new ResourceRequirement("read");

        requirement.Action.Should().Be("read");
        requirement.ResourceType.Should().BeNull();
    }

    [Fact]
    public void ResourceRequirement_ActionAndResourceType_SetsBoth()
    {
        var requirement = new ResourceRequirement("admin", "User");

        requirement.Action.Should().Be("admin");
        requirement.ResourceType.Should().Be("User");
    }

    [Fact]
    public void ResourceRequirement_ImplementsIAuthorizationRequirement()
    {
        var requirement = new ResourceRequirement("read");

        requirement.Should().BeAssignableTo<IAuthorizationRequirement>();
    }

    #endregion

    #region OwnershipRequirement

    [Fact]
    public void OwnershipRequirement_DefaultConstructor_NullResourceType()
    {
        var requirement = new OwnershipRequirement();

        requirement.ResourceType.Should().BeNull();
    }

    [Fact]
    public void OwnershipRequirement_WithResourceType_SetsResourceType()
    {
        var requirement = new OwnershipRequirement("Job");

        requirement.ResourceType.Should().Be("Job");
    }

    [Fact]
    public void OwnershipRequirement_ImplementsIAuthorizationRequirement()
    {
        var requirement = new OwnershipRequirement();

        requirement.Should().BeAssignableTo<IAuthorizationRequirement>();
    }

    #endregion

    #region AddResourceAuthorization — DI Registration

    [Fact]
    public void AddResourceAuthorization_RegistersPermissionResolver()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddResourceAuthorization(configuration);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IPermissionResolver));

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(PermissionResolver));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddResourceAuthorization_RegistersResourceAuthorizationHandler()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddResourceAuthorization(configuration);

        var descriptors = services
            .Where(d => d.ServiceType == typeof(IAuthorizationHandler))
            .ToList();

        descriptors.Should().Contain(d =>
            d.ImplementationType == typeof(ResourceAuthorizationHandler));
        descriptors.Should().Contain(d =>
            d.ImplementationType == typeof(OwnershipAuthorizationHandler));
    }

    [Fact]
    public void AddResourceAuthorization_HandlersRegisteredAsScoped()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddResourceAuthorization(configuration);

        var ownHandlerTypes = new[]
        {
            typeof(ResourceAuthorizationHandler),
            typeof(OwnershipAuthorizationHandler)
        };

        var handlerDescriptors = services
            .Where(d => d.ServiceType == typeof(IAuthorizationHandler)
                        && d.ImplementationType != null
                        && ownHandlerTypes.Contains(d.ImplementationType))
            .ToList();

        handlerDescriptors.Should().HaveCount(2);
        handlerDescriptors.Should().OnlyContain(d =>
            d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddResourceAuthorization_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var result = services.AddResourceAuthorization(configuration);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddAuthorizationMiddleware_RegistersResourceAuthorizationMiddleware()
    {
        var services = new ServiceCollection();

        var result = services.AddAuthorizationMiddleware();

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ResourceAuthorizationMiddleware));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
        result.Should().BeSameAs(services);
    }

    #endregion
}

[Trait("Category", "Unit")]
public class InMemoryResourceAuthorizationServiceTests
{
    private readonly IPermissionResolver _permissionResolver;
    private readonly InMemoryResourceAuthorizationService _sut;

    public InMemoryResourceAuthorizationServiceTests()
    {
        _permissionResolver = Substitute.For<IPermissionResolver>();
        _sut = new InMemoryResourceAuthorizationService(_permissionResolver);
    }

    private static ClaimsPrincipal CreateUser(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    #region GrantPermissionAsync + HasPermissionAsync

    [Fact]
    public async Task GrantPermissionAsync_ThenHasPermission_ReturnsTrue()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "job:read", "Job", "job-1");

        has.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithoutGrant_ReturnsFalse()
    {
        var has = await _sut.HasPermissionAsync("user-1", "job:read", "Job", "job-1");

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_NullResourceType_ReturnsFalse()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "job:read", null, null);

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WildcardResourceId_MatchesWildcardGrant()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "*", "job:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "job:read", "Job", null);

        has.Should().BeTrue();
    }

    [Fact]
    public async Task GrantPermissionAsync_DuplicatePermission_DoesNotDuplicate()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var permissions = await _sut.GetUserPermissionsAsync("user-1", "Job", "job-1");

        permissions.Should().ContainSingle().Which.Should().Be("job:read");
    }

    [Fact]
    public async Task GrantPermissionAsync_MultiplePermissions_AllStored()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:update", "admin");

        var permissions = await _sut.GetUserPermissionsAsync("user-1", "Job", "job-1");

        permissions.Should().HaveCount(2);
        permissions.Should().Contain("job:read");
        permissions.Should().Contain("job:update");
    }

    #endregion

    #region RevokePermissionAsync

    [Fact]
    public async Task RevokePermissionAsync_ExistingPermission_RemovesIt()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");
        await _sut.RevokePermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "job:read", "Job", "job-1");

        has.Should().BeFalse();
    }

    [Fact]
    public async Task RevokePermissionAsync_NonExistentPermission_DoesNotThrow()
    {
        var act = () => _sut.RevokePermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokePermissionAsync_OnlyRevokesSpecified_LeavesOthers()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:update", "admin");

        await _sut.RevokePermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "job:update", "Job", "job-1");
        has.Should().BeTrue();
    }

    #endregion

    #region GetUserPermissionsAsync

    [Fact]
    public async Task GetUserPermissionsAsync_NoPermissions_ReturnsEmpty()
    {
        var permissions = await _sut.GetUserPermissionsAsync("user-1", "Job", "job-1");

        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_DifferentResource_ReturnsEmpty()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var permissions = await _sut.GetUserPermissionsAsync("user-1", "User", "user-1");

        permissions.Should().BeEmpty();
    }

    #endregion

    #region IsResourceOwnerAsync

    [Fact]
    public async Task IsResourceOwnerAsync_NoOwnerRegistered_ReturnsFalse()
    {
        var isOwner = await _sut.IsResourceOwnerAsync("user-1", "Job", "job-1");

        isOwner.Should().BeFalse();
    }

    #endregion

    #region GetAccessibleResourcesAsync

    [Fact]
    public async Task GetAccessibleResourcesAsync_WithPermissions_ReturnsMatchingResources()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Job", "job-2", "job:update", "admin");
        await _sut.GrantPermissionAsync("user-1", "User", "user-2", "user:read", "admin");

        var resources = (await _sut.GetAccessibleResourcesAsync("user-1", "Job")).ToList();

        resources.Should().HaveCount(2);
        resources.Should().Contain(r => r.ResourceId == "job-1");
        resources.Should().Contain(r => r.ResourceId == "job-2");
        resources.Should().AllSatisfy(r => r.ResourceType.Should().Be("Job"));
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_NoPermissions_ReturnsEmpty()
    {
        var resources = await _sut.GetAccessibleResourcesAsync("user-1", "Job");

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_DifferentUser_ReturnsEmpty()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");

        var resources = await _sut.GetAccessibleResourcesAsync("user-2", "Job");

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_IncludesPermissionsInResult()
    {
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Job", "job-1", "job:update", "admin");

        var resources = (await _sut.GetAccessibleResourcesAsync("user-1", "Job")).ToList();

        resources.Should().ContainSingle();
        resources[0].Permissions.Should().Contain("job:read");
        resources[0].Permissions.Should().Contain("job:update");
    }

    #endregion

    #region AuthorizeAsync

    [Fact]
    public async Task AuthorizeAsync_NoUserIdClaim_ReturnsFailure()
    {
        var user = CreateAnonymousUser();

        var result = await _sut.AuthorizeAsync(user, "Job", "read");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain("User ID not found");
    }

    [Fact]
    public async Task AuthorizeAsync_NoRequiredPermissions_FailsClosed()
    {
        var user = CreateUser("user-1");
        _permissionResolver.GetRequiredPermissionsAsync("Job", "read")
            .Returns(Enumerable.Empty<PermissionDefinition>());

        var result = await _sut.AuthorizeAsync(user, "Job", "read");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain(r => r.Contains("No permissions defined"));
    }

    [Fact]
    public async Task AuthorizeAsync_HasAllRequiredPermissions_Succeeds()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "job:read" };
        _permissionResolver.GetRequiredPermissionsAsync("Job", "read")
            .Returns(new[] { requiredPermission });

        await _sut.GrantPermissionAsync("user-1", "Job", "*", "job:read", "admin");

        var result = await _sut.AuthorizeAsync(user, "Job", "read");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_MissingRequiredPermission_Fails()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "job:delete" };
        _permissionResolver.GetRequiredPermissionsAsync("Job", "delete")
            .Returns(new[] { requiredPermission });

        var result = await _sut.AuthorizeAsync(user, "Job", "delete");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain(r => r.Contains("job:delete"));
    }

    [Fact]
    public async Task AuthorizeAsync_WithResourceData_UsesResourceId()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "job:read" };
        _permissionResolver.GetRequiredPermissionsAsync("Job", "read")
            .Returns(new[] { requiredPermission });

        var resourceData = new { Id = "job-42" };
        await _sut.GrantPermissionAsync("user-1", "Job", "job-42", "job:read", "admin");

        var result = await _sut.AuthorizeAsync(user, "Job", "read", resourceData);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_WithResourceDataMismatch_Fails()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "job:read" };
        _permissionResolver.GetRequiredPermissionsAsync("Job", "read")
            .Returns(new[] { requiredPermission });

        var resourceData = new { Id = "job-42" };
        await _sut.GrantPermissionAsync("user-1", "Job", "job-99", "job:read", "admin");

        var result = await _sut.AuthorizeAsync(user, "Job", "read", resourceData);

        result.Succeeded.Should().BeFalse();
    }

    #endregion
}

[Trait("Category", "Unit")]
public class ResourceAuthorizationHandlerTests
{
    private readonly IResourceAuthorizationService _authService;
    private readonly ResourceAuthorizationHandler _sut;

    public ResourceAuthorizationHandlerTests()
    {
        _authService = Substitute.For<IResourceAuthorizationService>();
        _sut = new ResourceAuthorizationHandler(_authService, TestResourceMap.Instance);
    }

    private static ClaimsPrincipal CreateUser(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static AuthorizationHandlerContext CreateContext(
        ClaimsPrincipal user,
        ResourceRequirement requirement,
        object? resource = null)
    {
        return new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            resource);
    }

    [Fact]
    public async Task HandleAsync_WithResourceType_CallsAuthorizeService()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read", "Job");
        var context = CreateContext(user, requirement);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Job",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ServiceReturnsFail_ContextFails()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("delete", "Job");
        var context = CreateContext(user, requirement);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Job",
            "delete",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Fail("Denied"));

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoResourceType_AndNoFilterContext_Fails()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read"); // no resource type
        var context = CreateContext(user, requirement); // no AuthorizationFilterContext resource

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ResourceTypeFromFilterContext_ExtractsControllerName()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read"); // no resource type on requirement

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Jobs";
        var actionDescriptor = new ActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            filterContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Jobs",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }
}

[Trait("Category", "Unit")]
public class OwnershipAuthorizationHandlerTests
{
    private readonly IResourceAuthorizationService _authService;
    private readonly OwnershipAuthorizationHandler _sut;

    public OwnershipAuthorizationHandlerTests()
    {
        _authService = Substitute.For<IResourceAuthorizationService>();
        _sut = new OwnershipAuthorizationHandler(_authService, TestResourceMap.Instance);
    }

    private static ClaimsPrincipal CreateUser(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    [Fact]
    public async Task HandleAsync_NoUserIdClaim_Fails()
    {
        var user = CreateAnonymousUser();
        var requirement = new OwnershipRequirement("Job");
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, null);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoResourceType_Fails()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(); // null resource type
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, null); // no filter context

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_IsOwner_Succeeds()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Job");

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Jobs";
        routeData.Values["id"] = "job-1";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        _authService.IsResourceOwnerAsync("user-1", "Job", "job-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NotOwner_Fails()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Job");

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Jobs";
        routeData.Values["id"] = "job-1";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        _authService.IsResourceOwnerAsync("user-1", "Job", "job-1", Arg.Any<CancellationToken>())
            .Returns(false);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ResourceTypeFromFilterContext_WhenRequirementHasNone()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(); // no resource type

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Bookings";
        routeData.Values["id"] = "appt-1";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        _authService.IsResourceOwnerAsync("user-1", "Bookings", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoResourceId_InFilterContext_Fails()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Job");

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Jobs";
        // Note: no "id" in route data
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    #region Minimal API — HttpContext resource

    [Fact]
    public async Task HandleAsync_MinimalApi_HttpContext_WithId_Succeeds()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Job");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["id"] = "job-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Job", "job-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_HttpContext_WithBookingId_Succeeds()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Booking");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["bookingId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Booking", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_HttpContext_WithRequestId_Succeeds()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Match");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["requestId"] = "req-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Match", "req-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_HttpContext_WithUserId_Succeeds()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("User");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["userId"] = "target-user";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "User", "target-user", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_ResourceAwareDisambiguation_PicksPreferredParam()
    {
        // Real route: /users/calendar/{userId}/sync/{bookingId}
        // With resource type "Booking", bookingId is preferred over userId
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Booking");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["bookingId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Booking", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_MatchType_PicksRequestId_OverId()
    {
        // Real route: /referrals/requests/{requestId} — Match type prefers requestId
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Match");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["id"] = "id-1";
        httpContext.Request.RouteValues["requestId"] = "req-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Match", "req-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_NoResourceType_AmbiguousIds_FailsClosed()
    {
        // When resource type is null (no OwnershipRequirement type), generic resolution
        // with multiple known params is ambiguous → fail closed
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(null!);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["bookingId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_NoRouteId_FailsClosed()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Job");

        var httpContext = new DefaultHttpContext();
        // no route values with known ID params

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_UserCalendarSyncRoute_InfersBooking()
    {
        // Real route: /users/calendar/{userId}/sync/{bookingId}
        // Route values: bookingId → Booking (typed param inference)
        // Path segments: users → User (segment inference)
        // Route-value type (Booking) conflicts with segment type (User) → fail closed
        // BUT: route-value inference finds "bookingId" → Booking
        //      path inference finds "users" → User
        //      conflict → null → fail closed
        // Wait — actually the design says route-value wins when no conflict, fail-closed on conflict.
        // Let me re-read: the route-value type is Booking, path type is User → conflict → null.
        // That means this route ALSO fails closed without explicit annotation.
        // That's correct behavior — the caller must use OwnershipRequirement("Booking").
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(); // no explicit resource type

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/users/calendar/user-1/sync/appt-1";
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["bookingId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().IsResourceOwnerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_BookingRoute_InfersFromRouteValues()
    {
        // Real route: /bookings/{bookingId}/accept
        // Route values: bookingId → Booking
        // Path segments: bookings → Booking
        // Both agree → Booking + bookingId
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(); // no explicit resource type

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/bookings/appt-1/accept";
        httpContext.Request.RouteValues["bookingId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Booking", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_MultiResourceSegmentPath_FailsClosed()
    {
        // /jobs/user/{userId} → jobs→Job + user→User → ambiguous segments → null path type
        // Route values: only userId → no specific typed param → null route-value type
        // Result: null → fail closed
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/jobs/user/user-1";
        httpContext.Request.RouteValues["userId"] = "user-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().IsResourceOwnerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_AdminUsersPath_FailsClosed()
    {
        // /api/admin/users/{userId} → admin→System + users→User → ambiguous → null path type
        // Route values: only userId → no specific type → null route-value type
        // Result: null → fail closed
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/admin/users/user-1";
        httpContext.Request.RouteValues["userId"] = "user-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().IsResourceOwnerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_ExplicitType_OnMultiResourcePath_Works()
    {
        // Even on a multi-resource path, explicit resource type in requirement still works
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Job"); // explicit

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/jobs/user/user-1";
        httpContext.Request.RouteValues["jobId"] = "job-1";
        httpContext.Request.RouteValues["userId"] = "user-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Job", "job-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_ExplicitType_OnUserCalendarSyncRoute_Works()
    {
        // Explicit OwnershipRequirement("Booking") on /users/calendar/{userId}/sync/{bookingId}
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Booking");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/users/calendar/user-1/sync/appt-1";
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["bookingId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Booking", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    #endregion
}

#region ResourceAuthorizationHandler — Minimal API path inference

[Trait("Category", "Unit")]
public class ResourceAuthorizationHandlerMinimalApiTests
{
    private readonly IResourceAuthorizationService _authService;
    private readonly ResourceAuthorizationHandler _sut;

    public ResourceAuthorizationHandlerMinimalApiTests()
    {
        _authService = Substitute.For<IResourceAuthorizationService>();
        _sut = new ResourceAuthorizationHandler(_authService, TestResourceMap.Instance);
    }

    private static ClaimsPrincipal CreateUser(string userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_BareBookingsPath_InfersBooking()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/bookings/appt-1/accept";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Booking",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_BareJobsPath_InfersJob()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/jobs/42";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Job",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_BareMatchRequestsPath_InfersMatch()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/referrals/requests/req-1/reject";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Match",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_BareNotificationsPath_InfersNotification()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/notifications/n-1/read";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Notification",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_PaymentsPath_FailsClosed_NotDefinedResource()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/payments/pay-1/status";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        // Payment is not in GirderResources → InferResourceTypeFromPath returns null → fail
        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_MultiResourcePath_FailsClosed()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/admin/users/user-1"; // admin→System + users→User

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        // Ambiguous multi-resource path → null → fail closed
        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_ApiPrefixedSessionPath_InfersSession()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/session/sessions/s-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Session",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_UnknownPath_FailsClosed()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/unknown-resource/42";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ExplicitResourceType_OverridesPathInference()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read", "System");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/jobs/42"; // would infer Job but explicit wins

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "System",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Abstractions.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NullResource_FailsClosed()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, null);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }
}

#endregion

#region ResolveResourceId static method tests

[Trait("Category", "Unit")]
public class ResolveResourceIdTests
{
    // --- Generic resolution (no resource type) ---

    [Fact]
    public void ResolveResourceId_Generic_SingleKnownParam_ReturnsValue()
    {
        var routeValues = new RouteValueDictionary { { "id", "42" } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().Be("42");
    }

    [Theory]
    [InlineData("bookingId", "appt-1")]
    [InlineData("requestId", "req-1")]
    [InlineData("referralId", "match-1")]
    [InlineData("sessionId", "session-1")]
    [InlineData("userId", "user-1")]
    [InlineData("jobId", "job-1")]
    [InlineData("postingId", "posting-1")]
    [InlineData("notificationId", "notif-1")]
    [InlineData("paymentId", "pay-1")]
    [InlineData("templateId", "tmpl-1")]
    public void ResolveResourceId_Generic_EachKnownParam_ReturnsValue(string paramName, string paramValue)
    {
        var routeValues = new RouteValueDictionary { { paramName, paramValue } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().Be(paramValue);
    }

    [Fact]
    public void ResolveResourceId_Generic_MultipleKnownParams_ReturnsNull_AmbiguousFail()
    {
        var routeValues = new RouteValueDictionary
        {
            { "userId", "user-1" },
            { "bookingId", "appt-1" }
        };
        // No resourceType → generic → ambiguous → null
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void ResolveResourceId_Generic_NoKnownParams_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { { "action", "create" }, { "controller", "Jobs" } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void ResolveResourceId_Generic_KnownParamWithNullValue_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { { "id", null } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void ResolveResourceId_Generic_KnownParamWithEmptyString_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { { "id", "" } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void ResolveResourceId_Generic_UnknownParam_NotConfused()
    {
        var routeValues = new RouteValueDictionary
        {
            { "someOtherId", "val" },
            { "id", "42" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().Be("42");
    }

    // --- Resource-aware resolution ---

    [Fact]
    public void ResolveResourceId_Booking_PrefersBookingId_OverUserId()
    {
        // Real route: /users/calendar/{userId}/sync/{bookingId}
        var routeValues = new RouteValueDictionary
        {
            { "userId", "user-1" },
            { "bookingId", "appt-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Booking").Should().Be("appt-1");
    }

    [Fact]
    public void ResolveResourceId_Match_PrefersMatchId()
    {
        // Real route: /referrals/{referralId}/...
        var routeValues = new RouteValueDictionary
        {
            { "referralId", "match-1" },
            { "id", "id-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Match").Should().Be("match-1");
    }

    [Fact]
    public void ResolveResourceId_Match_FallsToRequestId()
    {
        // Real route: /referrals/requests/{requestId}/reject
        var routeValues = new RouteValueDictionary
        {
            { "requestId", "req-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Match").Should().Be("req-1");
    }

    [Fact]
    public void ResolveResourceId_Job_PrefersJobId()
    {
        var routeValues = new RouteValueDictionary
        {
            { "jobId", "job-1" },
            { "userId", "user-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Job").Should().Be("job-1");
    }

    [Fact]
    public void ResolveResourceId_Job_FallsToPostingId()
    {
        // Real route: /postings/{id} in JobService
        var routeValues = new RouteValueDictionary
        {
            { "postingId", "posting-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Job").Should().Be("posting-1");
    }

    [Fact]
    public void ResolveResourceId_Session_PrefersSessionId()
    {
        // Real route: /api/calls/{sessionId}
        var routeValues = new RouteValueDictionary
        {
            { "sessionId", "sess-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Session").Should().Be("sess-1");
    }

    [Fact]
    public void ResolveResourceId_Notification_PrefersNotificationId()
    {
        // Real route: /notifications/{notificationId}/read
        var routeValues = new RouteValueDictionary
        {
            { "notificationId", "notif-1" },
            { "userId", "user-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Notification").Should().Be("notif-1");
    }

    [Fact]
    public void ResolveResourceId_Notification_FallsToTemplateId()
    {
        // Real route: /templates/{templateId}
        var routeValues = new RouteValueDictionary
        {
            { "templateId", "tmpl-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Notification").Should().Be("tmpl-1");
    }

    [Fact]
    public void ResolveResourceId_UnknownResourceType_FallsToGeneric()
    {
        var routeValues = new RouteValueDictionary { { "id", "42" } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "UnknownType").Should().Be("42");
    }

    [Fact]
    public void ResolveResourceId_ResourceType_NoPreferredParamPresent_ReturnsNull()
    {
        // Booking expects bookingId or id — neither present
        var routeValues = new RouteValueDictionary { { "userId", "user-1" } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Booking").Should().BeNull();
    }
}

#endregion

#region InferResourceTypeFromPath static tests

[Trait("Category", "Unit")]
public class InferResourceTypeFromPathTests
{
    [Theory]
    // Real bare Minimal API routes — single resource type in path
    [InlineData("/bookings/appt-1/accept", "Booking")]
    [InlineData("/bookings", "Booking")]
    [InlineData("/referrals/requests/req-1/reject", "Match")]
    [InlineData("/referrals/m-1", "Match")]
    [InlineData("/referral-requests/req-1", "Match")]
    [InlineData("/jobs/42", "Job")]
    [InlineData("/postings/posting-1", "Job")]
    [InlineData("/notifications/n-1/read", "Notification")]
    [InlineData("/notifications", "Notification")]
    [InlineData("/preferences", "Notification")]
    [InlineData("/templates/tmpl-1", "Notification")]
    // /api/ prefixed routes (SessionService, UserService)
    [InlineData("/api/session/sessions/s-1", "Session")]
    [InlineData("/api/calls/sess-1", "Session")]
    [InlineData("/api/users/profile/me", "User")]
    [InlineData("/api/auth/login", "User")]
    [InlineData("/users/public/user-1", "User")]
    [InlineData("/users/management/user-1/roles", "User")]
    public void InferResourceTypeFromPath_SingleResourceRoutes_ReturnsCorrectType(string path, string expectedType)
    {
        ResourceAuthorizationHandler.InferResourceTypeFromPath(path, TestResourceMap.Instance).Should().Be(expectedType);
    }

    [Theory]
    // Multi-resource paths: multiple distinct resource types → ambiguous → null
    [InlineData("/api/admin/users/user-1")]                // admin→System + users→User
    [InlineData("/jobs/user/user-1")]                    // jobs→Job + user→User
    [InlineData("/reviews/user/user-1/stats")]             // reviews→Booking + user→User
    public void InferResourceTypeFromPath_MultiResourceRoutes_ReturnsNull_Ambiguous(string path)
    {
        ResourceAuthorizationHandler.InferResourceTypeFromPath(path, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void InferResourceTypeFromPath_SingleSegmentResourceWithMultipleIdParams_InfersFromSegment()
    {
        // /users/calendar/{userId}/sync/{bookingId} — only "users" maps to a resource type
        // at the path-segment level. Path inference returns User.
        // However, ResolveResourceTypeFromHttpContext will ALSO check route values,
        // where "bookingId" maps to Booking — which takes priority over segment inference.
        ResourceAuthorizationHandler
            .InferResourceTypeFromPath("/users/calendar/user-1/sync/appt-1", TestResourceMap.Instance)
            .Should().Be("User");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_BookingId_InfersBooking()
    {
        var routeValues = new RouteValueDictionary
        {
            { "userId", "user-1" },
            { "bookingId", "appt-1" }
        };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Booking");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_MatchId_InfersMatch()
    {
        var routeValues = new RouteValueDictionary { { "referralId", "match-1" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Match");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_RequestId_InfersMatch()
    {
        var routeValues = new RouteValueDictionary { { "requestId", "req-1" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Match");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_SessionId_InfersSession()
    {
        var routeValues = new RouteValueDictionary { { "sessionId", "sess-1" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Session");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_NotificationId_InfersNotification()
    {
        var routeValues = new RouteValueDictionary { { "notificationId", "notif-1" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Notification");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_GenericIdOnly_ReturnsNull()
    {
        // "id" is too generic — does not infer any resource type
        var routeValues = new RouteValueDictionary { { "id", "42" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_UserIdOnly_ReturnsNull()
    {
        // "userId" is too generic — does not infer any resource type
        var routeValues = new RouteValueDictionary { { "userId", "user-1" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_ConflictingTypedParams_ReturnsNull()
    {
        // bookingId→Booking + referralId→Match → conflict → null
        var routeValues = new RouteValueDictionary
        {
            { "bookingId", "appt-1" },
            { "referralId", "match-1" }
        };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().BeNull();
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_NoTypedParams_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { { "action", "create" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("/unknown-resource/42")]
    [InlineData("/health")]
    [InlineData("/")]
    [InlineData("")]
    // Payment is not a defined GirderResource
    [InlineData("/payments/pay-1/status")]
    public void InferResourceTypeFromPath_UnknownPaths_ReturnsNull(string path)
    {
        ResourceAuthorizationHandler.InferResourceTypeFromPath(path, TestResourceMap.Instance).Should().BeNull();
    }
}

#endregion

#region AuthorizationModule builder wiring tests

[Trait("Category", "Unit")]
public class AuthorizationModuleBuilderTests
{
    private static InfrastructureBuilder CreateBuilder(Dictionary<string, string?>? config = null)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config ?? new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        return new InfrastructureBuilder(services, configuration, environment, "TestService");
    }

    [Fact]
    public void AddAuthorization_SetsFlag_RegistersRoleBasedHandlers()
    {
        var builder = CreateBuilder();

        builder.AddAuthorization();

        builder.AuthorizationEnabled.Should().BeTrue();
        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizationHandler) &&
            d.ImplementationType == typeof(ResourceOwnerHandler));
    }

    [Fact]
    public void AddResourceAuthorization_SetsFlag_RegistersResourceHandlers()
    {
        var builder = CreateBuilder();

        builder.AddResourceAuthorization();

        builder.AuthorizationEnabled.Should().BeTrue();
        builder.Services.Should().NotContain(d =>
            d.ServiceType == typeof(IResourceAuthorizationService),
            "where permissions are stored is chosen with a provider package");
        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IPermissionResolver));
        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizationHandler) &&
            d.ImplementationType == typeof(ResourceAuthorizationHandler));
        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizationHandler) &&
            d.ImplementationType == typeof(OwnershipAuthorizationHandler));
    }

    [Fact]
    public void AddAuthorization_And_AddResourceAuthorization_CanCoexist()
    {
        var builder = CreateBuilder();

        builder.AddAuthorization();
        builder.AddResourceAuthorization();

        builder.AuthorizationEnabled.Should().BeTrue();
        // Both role-based and resource-based handlers registered
        builder.Services.Should().Contain(d =>
            d.ImplementationType == typeof(ResourceOwnerHandler));
        builder.Services.Should().Contain(d =>
            d.ImplementationType == typeof(ResourceAuthorizationHandler));
    }
}

#endregion

#region Production registration path tests

[Trait("Category", "Unit")]
public class ProductionRegistrationPathTests
{
    [Fact]
    public void AddSharedInfrastructure_RegistersResourceAuthorizationHandler()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        services.AddSharedInfrastructure(configuration, environment, "TestService");

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizationHandler) &&
            d.ImplementationType == typeof(ResourceAuthorizationHandler));
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersOwnershipAuthorizationHandler()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        services.AddSharedInfrastructure(configuration, environment, "TestService");

        services.Should().Contain(d =>
            d.ServiceType == typeof(IAuthorizationHandler) &&
            d.ImplementationType == typeof(OwnershipAuthorizationHandler));
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersPermissionResolver()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        services.AddSharedInfrastructure(configuration, environment, "TestService");

        services.Should().Contain(d =>
            d.ServiceType == typeof(IPermissionResolver) &&
            d.ImplementationType == typeof(PermissionResolver));
    }

    [Fact]
    public void AddSharedInfrastructure_RegistersResourcePolicies()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        services.AddSharedInfrastructure(configuration, environment, "TestService");

        var provider = services.BuildServiceProvider();
        var authOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        authOptions.GetPolicy("ResourceRead").Should().NotBeNull();
        authOptions.GetPolicy("ResourceWrite").Should().NotBeNull();
        authOptions.GetPolicy("ResourceDelete").Should().NotBeNull();
        authOptions.GetPolicy("ResourceOwner").Should().NotBeNull();
    }
}

#endregion
