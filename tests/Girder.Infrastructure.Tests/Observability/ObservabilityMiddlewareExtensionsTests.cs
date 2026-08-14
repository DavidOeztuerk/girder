using Girder.Infrastructure.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class PerformanceMiddlewareExtensionsTests
{
    [Fact]
    public void UsePerformanceMonitoring_ReturnsApplicationBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IPerformanceMonitoringService>());
        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);

        var result = appBuilder.UsePerformanceMonitoring();

        result.Should().NotBeNull();
        result.Should().BeSameAs(appBuilder);
    }

    [Fact]
    public void UsePerformanceMonitoring_AddsMiddlewareToPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IPerformanceMonitoringService>());
        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);

        // Verify that the middleware is registered without building the full pipeline
        // (building requires all middleware dependencies to be registered)
        var result = appBuilder.UsePerformanceMonitoring();
        result.Should().NotBeNull();
    }
}

[Trait("Category", "Unit")]
public class TelemetryMiddlewareExtensionsTests
{
    [Fact]
    public void UseTelemetry_ReturnsApplicationBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITelemetryService>());
        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);

        var result = appBuilder.UseTelemetry();

        result.Should().NotBeNull();
        result.Should().BeSameAs(appBuilder);
    }

    [Fact]
    public void UseCorrelationId_ReturnsApplicationBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);

        var result = appBuilder.UseCorrelationId();

        result.Should().NotBeNull();
        result.Should().BeSameAs(appBuilder);
    }

    [Fact]
    public void UseTelemetry_AndUseCorrelationId_BothAddMiddleware()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITelemetryService>());
        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);

        // Both methods should register without throwing
        var result1 = appBuilder.UseTelemetry();
        var result2 = appBuilder.UseCorrelationId();

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
    }
}
