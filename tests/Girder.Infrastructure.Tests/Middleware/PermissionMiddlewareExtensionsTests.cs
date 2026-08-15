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
