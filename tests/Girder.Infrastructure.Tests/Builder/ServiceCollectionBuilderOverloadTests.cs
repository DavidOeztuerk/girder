using Infrastructure.Builder;
using Infrastructure.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class ServiceCollectionBuilderOverloadTests
{
    private readonly IServiceCollection _services = new ServiceCollection();
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();

    public ServiceCollectionBuilderOverloadTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        _environment.EnvironmentName.Returns("Development");
    }

    [Fact]
    public void AddSharedInfrastructure_BuilderOverload_InvokesCallback()
    {
        var callbackInvoked = false;

        _services.AddSharedInfrastructure(_configuration, _environment, "TestService", infra =>
        {
            callbackInvoked = true;
            infra.ServiceName.Should().Be("TestService");
        });

        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void AddSharedInfrastructure_BuilderOverload_ReturnsSameServiceCollection()
    {
        var result = _services.AddSharedInfrastructure(_configuration, _environment, "TestService", _ => { });

        result.Should().BeSameAs(_services);
    }

    [Fact]
    public void UseSharedInfrastructure_BuilderOverload_InvokesCallback()
    {
        var app = Substitute.For<IApplicationBuilder>();
        var callbackInvoked = false;

        app.UseSharedInfrastructure(_environment, "TestService", mw =>
        {
            callbackInvoked = true;
            mw.ServiceName.Should().Be("TestService");
        });

        callbackInvoked.Should().BeTrue();
    }

    [Fact]
    public void UseSharedInfrastructure_BuilderOverload_ReturnsSameAppBuilder()
    {
        var app = Substitute.For<IApplicationBuilder>();

        var result = app.UseSharedInfrastructure(_environment, "TestService", _ => { });

        result.Should().BeSameAs(app);
    }
}
