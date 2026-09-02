using System.Net;
using Girder.Core.Identity;
using Girder.Infrastructure.Builder.Modules;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Security.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Builder;

/// <summary>
/// Principal resolution is reachable from the same builder as every other
/// module, on both the registration and the pipeline side.
/// </summary>
/// <remarks>
/// It used to be two loose extension methods on <c>IServiceCollection</c> and
/// <c>IApplicationBuilder</c>, so a service composing through the builder had
/// no way to find it and read claims by hand instead.
/// </remarks>
[Trait("Category", "Unit")]
public class PrincipalModuleTests
{
    [Fact]
    public void The_module_registers_principal_resolution()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSharedInfrastructure(
            builder.Configuration, builder.Environment, "test",
            infrastructure => infrastructure.AddPrincipal());
        var app = builder.Build();

        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetService<ICurrentPrincipal>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IPrincipalFactory>().Should().NotBeNull();
    }

    /// <summary>
    /// Anonymous is a case, not a fault: only endpoints that require identity
    /// may refuse it, and they do that through <c>[Authorize]</c>.
    /// </summary>
    [Fact]
    public async Task An_anonymous_request_passes_through_without_a_principal()
    {
        using var host = await StartHostAsync();

        var response = await host.GetTestClient().GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("no principal");
    }

    private static Task<IHost> StartHostAsync() =>
        new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices((context, services) => services.AddSharedInfrastructure(
                    new ConfigurationBuilder().Build(),
                    context.HostingEnvironment,
                    "test",
                    infrastructure => infrastructure.AddPrincipal()))
                .Configure((context, app) =>
                {
                    app.UseGirder(
                        context.HostingEnvironment, "test", pipeline => pipeline.UsePrincipal());
                    app.Run(async http =>
                    {
                        var current = http.RequestServices.GetRequiredService<ICurrentPrincipal>();
                        await http.Response.WriteAsync(
                            current.Current is null ? "no principal" : "principal");
                    });
                }))
            .StartAsync();
}
