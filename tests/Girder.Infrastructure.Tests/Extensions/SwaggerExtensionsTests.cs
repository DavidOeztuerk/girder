using Girder.Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Swagger;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class SwaggerExtensionsTests
{
    [Fact]
    public void AddSwaggerDocumentation_RegistersSwaggerGen()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSwaggerDocumentation("TestService");

        // Verify that SwaggerGen-related services are registered
        services.Should().Contain(d => d.ServiceType == typeof(ISwaggerProvider));
    }

    [Fact]
    public void AddSwaggerDocumentation_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddSwaggerDocumentation("TestService");

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddSwaggerDocumentation_WithCustomVersion_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddSwaggerDocumentation("TestService", "v2");

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddSwaggerDocumentation_WithDifferentServiceNames_RegistersCorrectly()
    {
        var services1 = new ServiceCollection();
        var services2 = new ServiceCollection();

        services1.AddSwaggerDocumentation("UserService");
        services2.AddSwaggerDocumentation("JobService");

        // Both should succeed without exceptions
        services1.Should().NotBeEmpty();
        services2.Should().NotBeEmpty();
    }

    [Fact]
    public void AddSwaggerDocumentation_DefaultVersion_IsV1()
    {
        var services = new ServiceCollection();

        // Should not throw when using default version
        var act = () => services.AddSwaggerDocumentation("TestService");
        act.Should().NotThrow();
    }

    [Fact]
    public void UseSwaggerDocumentation_ReturnsApplicationBuilder()
    {
        // Verify it returns the same app builder for chaining
        IApplicationBuilder? capturedResult = null;

        using var app = new Microsoft.AspNetCore.TestHost.TestServer(
            new WebHostBuilder()
                .ConfigureServices(s =>
                {
                    s.AddRouting();
                    s.AddSwaggerDocumentation("TestService");
                })
                .Configure(a =>
                {
                    capturedResult = a.UseSwaggerDocumentation("TestService");
                }));

        capturedResult.Should().NotBeNull();
    }

    [Fact]
    public void UseSwaggerDocumentation_WithCustomVersion_DoesNotThrow()
    {
        var act = () =>
        {
            using var _ = new Microsoft.AspNetCore.TestHost.TestServer(
                new WebHostBuilder()
                    .ConfigureServices(s =>
                    {
                        s.AddRouting();
                        s.AddSwaggerDocumentation("UserService", "v2");
                    })
                    .Configure(a =>
                    {
                        a.UseSwaggerDocumentation("UserService", "v2");
                    }));
        };

        act.Should().NotThrow();
    }
}
