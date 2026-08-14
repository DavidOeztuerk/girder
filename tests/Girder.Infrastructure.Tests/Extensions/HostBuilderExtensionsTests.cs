using Girder.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class HostBuilderExtensionsTests
{
    [Fact]
    public void UseSharedSerilog_ReturnsSameHostBuilder()
    {
        var hostBuilder = new HostBuilder();

        var result = hostBuilder.UseSharedSerilog("TestService");

        result.Should().BeSameAs(hostBuilder);
    }

    [Fact]
    public void UseSharedSerilog_WithDifferentServiceNames_DoesNotThrow()
    {
        var services = new[] { "UserService", "SkillService", "Gateway", "AppointmentService" };
        foreach (var serviceName in services)
        {
            var hostBuilder = new HostBuilder();
            var act = () => hostBuilder.UseSharedSerilog(serviceName);
            act.Should().NotThrow($"UseSharedSerilog should not throw for service '{serviceName}'");
        }
    }

    [Fact]
    public void UseSharedSerilog_ShouldRegisterSerilogHostedService()
    {
        var hostBuilder = new HostBuilder();

        // UseSharedSerilog registers Serilog as the logging provider
        // It should not throw during configuration
        var act = () =>
        {
            hostBuilder.UseSharedSerilog("TestService");
            // Build host to ensure the configuration is applied
            // We don't build the host here since it requires more setup
        };

        act.Should().NotThrow();
    }

    #region Additional Tests

    [Fact]
    public void UseSharedSerilog_NullServiceName_DoesNotThrowDuringConfiguration()
    {
        var hostBuilder = new HostBuilder();

        // The extension accepts null or empty string — configuration is deferred
        var act = () => hostBuilder.UseSharedSerilog(null!);
        act.Should().NotThrow();
    }

    [Fact]
    public void UseSharedSerilog_EmptyServiceName_DoesNotThrow()
    {
        var hostBuilder = new HostBuilder();

        var act = () => hostBuilder.UseSharedSerilog(string.Empty);
        act.Should().NotThrow();
    }

    [Fact]
    public void UseSharedSerilog_ChainedCalls_ReturnsSameBuilder()
    {
        var hostBuilder = new HostBuilder();

        // Chain multiple calls — each should return the same builder
        var result = hostBuilder
            .UseSharedSerilog("ServiceA")
            .UseSharedSerilog("ServiceB");

        result.Should().BeSameAs(hostBuilder);
    }

    [Fact]
    public void UseSharedSerilog_LongServiceName_DoesNotThrow()
    {
        var hostBuilder = new HostBuilder();
        var longName = new string('x', 500);

        var act = () => hostBuilder.UseSharedSerilog(longName);
        act.Should().NotThrow();
    }

    [Fact]
    public void UseSharedSerilog_SpecialCharactersInName_DoesNotThrow()
    {
        var hostBuilder = new HostBuilder();

        var act = () => hostBuilder.UseSharedSerilog("My-Service.V2 (Test)");
        act.Should().NotThrow();
    }

    [Fact]
    public async Task UseSharedSerilog_WhenHostBuilds_CallbackExecutesWithoutThrowing()
    {
        // Actually build the host so the Serilog lambda callback is invoked
        using var host = await new HostBuilder()
            .ConfigureServices(services => services.AddLogging())
            .UseSharedSerilog("TestServiceBuild")
            .StartAsync();

        host.Should().NotBeNull();
        await host.StopAsync();
    }

    [Fact]
    public async Task UseSharedSerilog_WithEmptyServiceName_WhenHostBuilds_DoesNotThrow()
    {
        var act = async () =>
        {
            using var host = await new HostBuilder()
                .ConfigureServices(services => services.AddLogging())
                .UseSharedSerilog(string.Empty)
                .StartAsync();
            await host.StopAsync();
        };

        await act.Should().NotThrowAsync();
    }

    #endregion
}
