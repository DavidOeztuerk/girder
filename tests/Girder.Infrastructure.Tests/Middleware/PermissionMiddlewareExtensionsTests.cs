using Girder.Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Middleware;

[Trait("Category", "Unit")]
public class PermissionMiddlewareExtensionsTests
{
    [Fact]
    public void UsePermissionMiddleware_ReturnsApplicationBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();

        var serviceProvider = services.BuildServiceProvider();

        var appBuilder = new ApplicationBuilder(serviceProvider);

        var result = appBuilder.UsePermissionMiddleware();

        result.Should().NotBeNull();
        result.Should().BeSameAs(appBuilder);
    }

    [Fact]
    public void UsePermissionMiddleware_AddsMiddlewareToThePipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        var serviceProvider = services.BuildServiceProvider();

        var appBuilder = new ApplicationBuilder(serviceProvider);
        appBuilder.UsePermissionMiddleware();

        // Verify pipeline can be built without exceptions
        var act = () => appBuilder.Build();
        act.Should().NotThrow();
    }
}

[Trait("Category", "Unit")]
public class RequirePermissionAttributeTests
{
    [Fact]
    public void Constructor_WithPermissionOnly_SetsPermission()
    {
        var attr = new RequirePermissionAttribute("users:read");

        attr.Permission.Should().Be("users:read");
        attr.Resource.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithPermissionAndResource_SetsBoth()
    {
        var attr = new RequirePermissionAttribute("users:read", "user-resource");

        attr.Permission.Should().Be("users:read");
        attr.Resource.Should().Be("user-resource");
    }

    [Fact]
    public void Constructor_WithEmptyPermission_StoresEmptyString()
    {
        var attr = new RequirePermissionAttribute(string.Empty);

        attr.Permission.Should().BeEmpty();
    }

    [Fact]
    public void IsAttribute_CanBeUsedAsAttribute()
    {
        var type = typeof(RequirePermissionAttribute);
        type.Should().BeAssignableTo<Attribute>();
    }
}
