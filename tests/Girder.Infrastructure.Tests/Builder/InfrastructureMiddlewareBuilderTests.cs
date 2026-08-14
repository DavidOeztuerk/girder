using Girder.Infrastructure.Builder;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

namespace Girder.Infrastructure.Tests.Builder;

[Trait("Category", "Unit")]
public class InfrastructureMiddlewareBuilderTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var app = Substitute.For<IApplicationBuilder>();
        var env = Substitute.For<IHostEnvironment>();

        var builder = new InfrastructureMiddlewareBuilder(app, env, "TestService");

        builder.App.Should().BeSameAs(app);
        builder.Environment.Should().BeSameAs(env);
        builder.ServiceName.Should().Be("TestService");
    }
}
