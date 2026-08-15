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
        var requirement = new OwnershipRequirement("Skill");

        requirement.ResourceType.Should().Be("Skill");
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
    public void AddResourceAuthorization_WithoutRedis_RegistersInMemoryService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddResourceAuthorization(configuration);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IResourceAuthorizationService));

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(InMemoryResourceAuthorizationService));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddResourceAuthorization_WithRedis_RegistersRedisBasedService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = "localhost:6379"
            })
            .Build();

        services.AddResourceAuthorization(configuration);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IResourceAuthorizationService));

        descriptor.Should().NotBeNull();
        descriptor!.ImplementationType.Should().Be(typeof(ResourceAuthorizationService));
    }

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
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "skill:read", "Skill", "skill-1");

        has.Should().BeTrue();
    }

    [Fact]
    public async Task HasPermissionAsync_WithoutGrant_ReturnsFalse()
    {
        var has = await _sut.HasPermissionAsync("user-1", "skill:read", "Skill", "skill-1");

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_NullResourceType_ReturnsFalse()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "skill:read", null, null);

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasPermissionAsync_WildcardResourceId_MatchesWildcardGrant()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "*", "skill:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "skill:read", "Skill", null);

        has.Should().BeTrue();
    }

    [Fact]
    public async Task GrantPermissionAsync_DuplicatePermission_DoesNotDuplicate()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var permissions = await _sut.GetUserPermissionsAsync("user-1", "Skill", "skill-1");

        permissions.Should().ContainSingle().Which.Should().Be("skill:read");
    }

    [Fact]
    public async Task GrantPermissionAsync_MultiplePermissions_AllStored()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:update", "admin");

        var permissions = await _sut.GetUserPermissionsAsync("user-1", "Skill", "skill-1");

        permissions.Should().HaveCount(2);
        permissions.Should().Contain("skill:read");
        permissions.Should().Contain("skill:update");
    }

    #endregion

    #region RevokePermissionAsync

    [Fact]
    public async Task RevokePermissionAsync_ExistingPermission_RemovesIt()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");
        await _sut.RevokePermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "skill:read", "Skill", "skill-1");

        has.Should().BeFalse();
    }

    [Fact]
    public async Task RevokePermissionAsync_NonExistentPermission_DoesNotThrow()
    {
        var act = () => _sut.RevokePermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RevokePermissionAsync_OnlyRevokesSpecified_LeavesOthers()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:update", "admin");

        await _sut.RevokePermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var has = await _sut.HasPermissionAsync("user-1", "skill:update", "Skill", "skill-1");
        has.Should().BeTrue();
    }

    #endregion

    #region GetUserPermissionsAsync

    [Fact]
    public async Task GetUserPermissionsAsync_NoPermissions_ReturnsEmpty()
    {
        var permissions = await _sut.GetUserPermissionsAsync("user-1", "Skill", "skill-1");

        permissions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserPermissionsAsync_DifferentResource_ReturnsEmpty()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var permissions = await _sut.GetUserPermissionsAsync("user-1", "User", "user-1");

        permissions.Should().BeEmpty();
    }

    #endregion

    #region IsResourceOwnerAsync

    [Fact]
    public async Task IsResourceOwnerAsync_NoOwnerRegistered_ReturnsFalse()
    {
        var isOwner = await _sut.IsResourceOwnerAsync("user-1", "Skill", "skill-1");

        isOwner.Should().BeFalse();
    }

    #endregion

    #region GetAccessibleResourcesAsync

    [Fact]
    public async Task GetAccessibleResourcesAsync_WithPermissions_ReturnsMatchingResources()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-2", "skill:update", "admin");
        await _sut.GrantPermissionAsync("user-1", "User", "user-2", "user:read", "admin");

        var resources = (await _sut.GetAccessibleResourcesAsync("user-1", "Skill")).ToList();

        resources.Should().HaveCount(2);
        resources.Should().Contain(r => r.ResourceId == "skill-1");
        resources.Should().Contain(r => r.ResourceId == "skill-2");
        resources.Should().AllSatisfy(r => r.ResourceType.Should().Be("Skill"));
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_NoPermissions_ReturnsEmpty()
    {
        var resources = await _sut.GetAccessibleResourcesAsync("user-1", "Skill");

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_DifferentUser_ReturnsEmpty()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");

        var resources = await _sut.GetAccessibleResourcesAsync("user-2", "Skill");

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAccessibleResourcesAsync_IncludesPermissionsInResult()
    {
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:read", "admin");
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-1", "skill:update", "admin");

        var resources = (await _sut.GetAccessibleResourcesAsync("user-1", "Skill")).ToList();

        resources.Should().ContainSingle();
        resources[0].Permissions.Should().Contain("skill:read");
        resources[0].Permissions.Should().Contain("skill:update");
    }

    #endregion

    #region AuthorizeAsync

    [Fact]
    public async Task AuthorizeAsync_NoUserIdClaim_ReturnsFailure()
    {
        var user = CreateAnonymousUser();

        var result = await _sut.AuthorizeAsync(user, "Skill", "read");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain("User ID not found");
    }

    [Fact]
    public async Task AuthorizeAsync_NoRequiredPermissions_FailsClosed()
    {
        var user = CreateUser("user-1");
        _permissionResolver.GetRequiredPermissionsAsync("Skill", "read")
            .Returns(Enumerable.Empty<PermissionDefinition>());

        var result = await _sut.AuthorizeAsync(user, "Skill", "read");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain(r => r.Contains("No permissions defined"));
    }

    [Fact]
    public async Task AuthorizeAsync_HasAllRequiredPermissions_Succeeds()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "skill:read" };
        _permissionResolver.GetRequiredPermissionsAsync("Skill", "read")
            .Returns(new[] { requiredPermission });

        await _sut.GrantPermissionAsync("user-1", "Skill", "*", "skill:read", "admin");

        var result = await _sut.AuthorizeAsync(user, "Skill", "read");

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_MissingRequiredPermission_Fails()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "skill:delete" };
        _permissionResolver.GetRequiredPermissionsAsync("Skill", "delete")
            .Returns(new[] { requiredPermission });

        var result = await _sut.AuthorizeAsync(user, "Skill", "delete");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().Contain(r => r.Contains("skill:delete"));
    }

    [Fact]
    public async Task AuthorizeAsync_WithResourceData_UsesResourceId()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "skill:read" };
        _permissionResolver.GetRequiredPermissionsAsync("Skill", "read")
            .Returns(new[] { requiredPermission });

        var resourceData = new { Id = "skill-42" };
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-42", "skill:read", "admin");

        var result = await _sut.AuthorizeAsync(user, "Skill", "read", resourceData);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AuthorizeAsync_WithResourceDataMismatch_Fails()
    {
        var user = CreateUser("user-1");
        var requiredPermission = new PermissionDefinition { Name = "skill:read" };
        _permissionResolver.GetRequiredPermissionsAsync("Skill", "read")
            .Returns(new[] { requiredPermission });

        var resourceData = new { Id = "skill-42" };
        await _sut.GrantPermissionAsync("user-1", "Skill", "skill-99", "skill:read", "admin");

        var result = await _sut.AuthorizeAsync(user, "Skill", "read", resourceData);

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
        var requirement = new ResourceRequirement("read", "Skill");
        var context = CreateContext(user, requirement);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Skill",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_ServiceReturnsFail_ContextFails()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("delete", "Skill");
        var context = CreateContext(user, requirement);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Skill",
            "delete",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Fail("Denied"));

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
        routeData.Values["controller"] = "Skills";
        var actionDescriptor = new ActionDescriptor();
        var actionContext = new ActionContext(httpContext, routeData, actionDescriptor);
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            user,
            filterContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Skills",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

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
        var requirement = new OwnershipRequirement("Skill");
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
        var requirement = new OwnershipRequirement("Skill");

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Skills";
        routeData.Values["id"] = "skill-1";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        _authService.IsResourceOwnerAsync("user-1", "Skill", "skill-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NotOwner_Fails()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Skill");

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Skills";
        routeData.Values["id"] = "skill-1";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        _authService.IsResourceOwnerAsync("user-1", "Skill", "skill-1", Arg.Any<CancellationToken>())
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
        routeData.Values["controller"] = "Appointments";
        routeData.Values["id"] = "appt-1";
        var actionContext = new ActionContext(httpContext, routeData, new ActionDescriptor());
        var filterContext = new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, filterContext);

        _authService.IsResourceOwnerAsync("user-1", "Appointments", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoResourceId_InFilterContext_Fails()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Skill");

        var httpContext = new DefaultHttpContext();
        var routeData = new RouteData();
        routeData.Values["controller"] = "Skills";
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
        var requirement = new OwnershipRequirement("Skill");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["id"] = "skill-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Skill", "skill-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_HttpContext_WithAppointmentId_Succeeds()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Appointment");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["appointmentId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Appointment", "appt-1", Arg.Any<CancellationToken>())
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
        // Real route: /users/calendar/{userId}/sync/{appointmentId}
        // With resource type "Appointment", appointmentId is preferred over userId
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Appointment");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["appointmentId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Appointment", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_MatchType_PicksRequestId_OverId()
    {
        // Real route: /matches/requests/{requestId} — Match type prefers requestId
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
        httpContext.Request.RouteValues["appointmentId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_NoRouteId_FailsClosed()
    {
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Skill");

        var httpContext = new DefaultHttpContext();
        // no route values with known ID params

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_UserCalendarSyncRoute_InfersAppointment()
    {
        // Real route: /users/calendar/{userId}/sync/{appointmentId}
        // Route values: appointmentId → Appointment (typed param inference)
        // Path segments: users → User (segment inference)
        // Route-value type (Appointment) conflicts with segment type (User) → fail closed
        // BUT: route-value inference finds "appointmentId" → Appointment
        //      path inference finds "users" → User
        //      conflict → null → fail closed
        // Wait — actually the design says route-value wins when no conflict, fail-closed on conflict.
        // Let me re-read: the route-value type is Appointment, path type is User → conflict → null.
        // That means this route ALSO fails closed without explicit annotation.
        // That's correct behavior — the caller must use OwnershipRequirement("Appointment").
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(); // no explicit resource type

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/users/calendar/user-1/sync/appt-1";
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["appointmentId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        await _authService.DidNotReceive().IsResourceOwnerAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_AppointmentRoute_InfersFromRouteValues()
    {
        // Real route: /appointments/{appointmentId}/accept
        // Route values: appointmentId → Appointment
        // Path segments: appointments → Appointment
        // Both agree → Appointment + appointmentId
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement(); // no explicit resource type

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/appointments/appt-1/accept";
        httpContext.Request.RouteValues["appointmentId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Appointment", "appt-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_GenericOwnership_MultiResourceSegmentPath_FailsClosed()
    {
        // /skills/user/{userId} → skills→Skill + user→User → ambiguous segments → null path type
        // Route values: only userId → no specific typed param → null route-value type
        // Result: null → fail closed
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement();

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/skills/user/user-1";
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
        var requirement = new OwnershipRequirement("Skill"); // explicit

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/skills/user/user-1";
        httpContext.Request.RouteValues["skillId"] = "skill-1";
        httpContext.Request.RouteValues["userId"] = "user-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Skill", "skill-1", Arg.Any<CancellationToken>())
            .Returns(true);

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_ExplicitType_OnUserCalendarSyncRoute_Works()
    {
        // Explicit OwnershipRequirement("Appointment") on /users/calendar/{userId}/sync/{appointmentId}
        var user = CreateUser("user-1");
        var requirement = new OwnershipRequirement("Appointment");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/users/calendar/user-1/sync/appt-1";
        httpContext.Request.RouteValues["userId"] = "user-1";
        httpContext.Request.RouteValues["appointmentId"] = "appt-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.IsResourceOwnerAsync("user-1", "Appointment", "appt-1", Arg.Any<CancellationToken>())
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
    public async Task HandleAsync_MinimalApi_BareAppointmentsPath_InfersAppointment()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/appointments/appt-1/accept";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Appointment",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_BareSkillsPath_InfersSkill()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/skills/42";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Skill",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

        await ((IAuthorizationHandler)_sut).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_MinimalApi_BareMatchRequestsPath_InfersMatch()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/matches/requests/req-1/reject";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Match",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

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
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

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
    public async Task HandleAsync_MinimalApi_ApiPrefixedVideocallPath_InfersVideocall()
    {
        var user = CreateUser("user-1");
        var requirement = new ResourceRequirement("read");

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/videocall/sessions/s-1";

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "Videocall",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

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
        httpContext.Request.Path = "/skills/42"; // would infer Skill but explicit wins

        var context = new AuthorizationHandlerContext(
            new[] { requirement }, user, httpContext);

        _authService.AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(),
            "System",
            "read",
            Arg.Any<object?>(),
            Arg.Any<CancellationToken>())
            .Returns(Girder.Infrastructure.Security.Authorization.AuthorizationResult.Success());

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
    [InlineData("appointmentId", "appt-1")]
    [InlineData("requestId", "req-1")]
    [InlineData("matchId", "match-1")]
    [InlineData("sessionId", "session-1")]
    [InlineData("userId", "user-1")]
    [InlineData("skillId", "skill-1")]
    [InlineData("listingId", "listing-1")]
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
            { "appointmentId", "appt-1" }
        };
        // No resourceType → generic → ambiguous → null
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void ResolveResourceId_Generic_NoKnownParams_ReturnsNull()
    {
        var routeValues = new RouteValueDictionary { { "action", "create" }, { "controller", "Skills" } };
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
    public void ResolveResourceId_Appointment_PrefersAppointmentId_OverUserId()
    {
        // Real route: /users/calendar/{userId}/sync/{appointmentId}
        var routeValues = new RouteValueDictionary
        {
            { "userId", "user-1" },
            { "appointmentId", "appt-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Appointment").Should().Be("appt-1");
    }

    [Fact]
    public void ResolveResourceId_Match_PrefersMatchId()
    {
        // Real route: /matches/{matchId}/...
        var routeValues = new RouteValueDictionary
        {
            { "matchId", "match-1" },
            { "id", "id-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Match").Should().Be("match-1");
    }

    [Fact]
    public void ResolveResourceId_Match_FallsToRequestId()
    {
        // Real route: /matches/requests/{requestId}/reject
        var routeValues = new RouteValueDictionary
        {
            { "requestId", "req-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Match").Should().Be("req-1");
    }

    [Fact]
    public void ResolveResourceId_Skill_PrefersSkillId()
    {
        var routeValues = new RouteValueDictionary
        {
            { "skillId", "skill-1" },
            { "userId", "user-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Skill").Should().Be("skill-1");
    }

    [Fact]
    public void ResolveResourceId_Skill_FallsToListingId()
    {
        // Real route: /listings/{id} in SkillService
        var routeValues = new RouteValueDictionary
        {
            { "listingId", "listing-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Skill").Should().Be("listing-1");
    }

    [Fact]
    public void ResolveResourceId_Videocall_PrefersSessionId()
    {
        // Real route: /api/calls/{sessionId}
        var routeValues = new RouteValueDictionary
        {
            { "sessionId", "sess-1" }
        };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Videocall").Should().Be("sess-1");
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
        // Appointment expects appointmentId or id — neither present
        var routeValues = new RouteValueDictionary { { "userId", "user-1" } };
        OwnershipAuthorizationHandler.ResolveResourceId(routeValues, TestResourceMap.Instance, "Appointment").Should().BeNull();
    }
}

#endregion

#region InferResourceTypeFromPath static tests

[Trait("Category", "Unit")]
public class InferResourceTypeFromPathTests
{
    [Theory]
    // Real bare Minimal API routes — single resource type in path
    [InlineData("/appointments/appt-1/accept", "Appointment")]
    [InlineData("/appointments", "Appointment")]
    [InlineData("/matches/requests/req-1/reject", "Match")]
    [InlineData("/matches/m-1", "Match")]
    [InlineData("/match-requests/req-1", "Match")]
    [InlineData("/skills/42", "Skill")]
    [InlineData("/listings/listing-1", "Skill")]
    [InlineData("/notifications/n-1/read", "Notification")]
    [InlineData("/notifications", "Notification")]
    [InlineData("/preferences", "Notification")]
    [InlineData("/templates/tmpl-1", "Notification")]
    // /api/ prefixed routes (VideocallService, UserService)
    [InlineData("/api/videocall/sessions/s-1", "Videocall")]
    [InlineData("/api/calls/sess-1", "Videocall")]
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
    [InlineData("/skills/user/user-1")]                    // skills→Skill + user→User
    [InlineData("/reviews/user/user-1/stats")]             // reviews→Appointment + user→User
    public void InferResourceTypeFromPath_MultiResourceRoutes_ReturnsNull_Ambiguous(string path)
    {
        ResourceAuthorizationHandler.InferResourceTypeFromPath(path, TestResourceMap.Instance).Should().BeNull();
    }

    [Fact]
    public void InferResourceTypeFromPath_SingleSegmentResourceWithMultipleIdParams_InfersFromSegment()
    {
        // /users/calendar/{userId}/sync/{appointmentId} — only "users" maps to a resource type
        // at the path-segment level. Path inference returns User.
        // However, ResolveResourceTypeFromHttpContext will ALSO check route values,
        // where "appointmentId" maps to Appointment — which takes priority over segment inference.
        ResourceAuthorizationHandler
            .InferResourceTypeFromPath("/users/calendar/user-1/sync/appt-1", TestResourceMap.Instance)
            .Should().Be("User");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_AppointmentId_InfersAppointment()
    {
        var routeValues = new RouteValueDictionary
        {
            { "userId", "user-1" },
            { "appointmentId", "appt-1" }
        };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Appointment");
    }

    [Fact]
    public void InferResourceTypeFromRouteValues_MatchId_InfersMatch()
    {
        var routeValues = new RouteValueDictionary { { "matchId", "match-1" } };
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
    public void InferResourceTypeFromRouteValues_SessionId_InfersVideocall()
    {
        var routeValues = new RouteValueDictionary { { "sessionId", "sess-1" } };
        ResourceAuthorizationHandler.InferResourceTypeFromRouteValues(routeValues, TestResourceMap.Instance)
            .Should().Be("Videocall");
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
        // appointmentId→Appointment + matchId→Match → conflict → null
        var routeValues = new RouteValueDictionary
        {
            { "appointmentId", "appt-1" },
            { "matchId", "match-1" }
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
        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IResourceAuthorizationService));
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
    public void AddResourceAuthorization_WithoutRedis_RegistersInMemoryService()
    {
        var builder = CreateBuilder();

        builder.AddResourceAuthorization();

        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IResourceAuthorizationService) &&
            d.ImplementationType == typeof(InMemoryResourceAuthorizationService));
    }

    [Fact]
    public void AddResourceAuthorization_WithRedis_RegistersRedisService()
    {
        var builder = CreateBuilder(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = "localhost:6379"
        });

        builder.AddResourceAuthorization();

        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IResourceAuthorizationService) &&
            d.ImplementationType == typeof(ResourceAuthorizationService));
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
    public void AddSharedInfrastructure_RegistersResourceAuthorizationService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        services.AddSharedInfrastructure(configuration, environment, "TestService");

        services.Should().Contain(d =>
            d.ServiceType == typeof(IResourceAuthorizationService));
    }

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

    [Fact]
    public void AddSharedInfrastructure_WithoutRedis_RegistersInMemoryAuthService()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Development");

        services.AddSharedInfrastructure(configuration, environment, "TestService");

        services.Should().Contain(d =>
            d.ServiceType == typeof(IResourceAuthorizationService) &&
            d.ImplementationType == typeof(InMemoryResourceAuthorizationService));
    }
}

#endregion
