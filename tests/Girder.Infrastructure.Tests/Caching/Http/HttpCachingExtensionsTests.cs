using Girder.Application.Interfaces;
using Girder.Infrastructure.Caching.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Caching.Http;

[Trait("Category", "Unit")]
public class HttpCachingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddHttpResponseCaching_WithConfiguration_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var config = new ConfigurationBuilder().Build();

        services.AddHttpResponseCaching(config);

        // Verify service descriptors are registered (avoid resolving to prevent needing full DI graph)
        services.Should().Contain(d => d.ServiceType == typeof(ICachePolicyProvider));
        services.Should().Contain(d => d.ServiceType == typeof(IETagGenerator));
    }

    [Fact]
    public void AddHttpResponseCaching_WithConfiguration_ReturnsSameCollection()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var result = services.AddHttpResponseCaching(config);

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddHttpResponseCaching_WithConfigureAction_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddHttpResponseCaching(options =>
        {
            options.Enabled = true;
            options.DefaultPublicMaxAge = 300;
        });

        // Verify service descriptors are registered (avoid resolving to prevent needing full DI graph)
        services.Should().Contain(d => d.ServiceType == typeof(ICachePolicyProvider));
        services.Should().Contain(d => d.ServiceType == typeof(IETagGenerator));
    }

    [Fact]
    public void AddHttpResponseCaching_WithConfigureAction_AppliesOptions()
    {
        var services = new ServiceCollection();

        services.AddHttpResponseCaching(options =>
        {
            options.Enabled = false;
            options.DefaultPublicMaxAge = 600;
        });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HttpCachingOptions>>().Value;

        options.Enabled.Should().BeFalse();
        options.DefaultPublicMaxAge.Should().Be(600);
    }

    [Fact]
    public void AddHttpResponseCaching_WithConfigureAction_ReturnsSameCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddHttpResponseCaching(_ => { });

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddHttpResponseCaching_RegistersCachePolicyProviderAsSingleton()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddHttpResponseCaching(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICachePolicyProvider));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddHttpResponseCaching_RegistersETagGeneratorAsSingleton()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        services.AddLogging();
        services.AddHttpResponseCaching(config);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IETagGenerator));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}

[Trait("Category", "Unit")]
public class HttpCachingApplicationBuilderExtensionsTests
{
    [Fact]
    public void UseHttpResponseCachingHeaders_ReturnsApplicationBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<ICachePolicyProvider>(Substitute.For<ICachePolicyProvider>());
        services.AddSingleton<IETagGenerator>(Substitute.For<IETagGenerator>());
        services.Configure<HttpCachingOptions>(_ => { });

        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);

        var result = appBuilder.UseHttpResponseCachingHeaders();

        result.Should().NotBeNull();
        result.Should().BeSameAs(appBuilder);
    }

    [Fact]
    public void UseHttpResponseCachingHeaders_AddsMiddlewareToPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<ICachePolicyProvider>(Substitute.For<ICachePolicyProvider>());
        services.AddSingleton<IETagGenerator>(Substitute.For<IETagGenerator>());
        services.Configure<HttpCachingOptions>(_ => { });

        var serviceProvider = services.BuildServiceProvider();
        var appBuilder = new ApplicationBuilder(serviceProvider);
        appBuilder.UseHttpResponseCachingHeaders();

        var act = () => appBuilder.Build();
        act.Should().NotThrow();
    }
}
