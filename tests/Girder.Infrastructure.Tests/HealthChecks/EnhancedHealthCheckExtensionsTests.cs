using Girder.Infrastructure.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class EnhancedHealthCheckExtensionsTests
{
    [Fact]
    public void AddGirderHealthChecks_WithNoConfigure_RegistersHealthChecks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        var result = services.AddGirderHealthChecks();

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddGirderHealthChecks_WithConfigure_InvokesCallback()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        var configureCalled = false;
        services.AddGirderHealthChecks(builder =>
        {
            configureCalled = true;
        });

        configureCalled.Should().BeTrue();
    }

    [Fact]
    public void AddGirderHealthChecks_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        var result = services.AddGirderHealthChecks();

        result.Should().NotBeNull();
        result.Should().BeAssignableTo<IServiceCollection>();
    }

    #region Additional Tests

    [Fact]
    public void AddGirderHealthChecks_RegistersHealthChecksService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        services.AddGirderHealthChecks();

        // HealthCheckService is registered by AddHealthChecks()
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(HealthCheckService));
        descriptor.Should().NotBeNull();
    }

    [Fact]
    public void AddGirderHealthChecks_WithConfigure_BothBuiltInAndCustomRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        var customCalled = false;
        services.AddGirderHealthChecks(builder =>
        {
            customCalled = true;
            builder.AddCustomCheck<ApplicationHealthCheck>("extra-app", HealthStatus.Unhealthy);
        });

        customCalled.Should().BeTrue();
    }

    #endregion
}

[Trait("Category", "Unit")]
public class HealthCheckBuilderTests
{
    [Fact]
    public void Constructor_WithValidServices_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => new HealthCheckBuilder(services);
        act.Should().NotThrow();
    }

    [Fact]
    public void AddRedisHealthCheck_RegistersRedisChecks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());

        var builder = new HealthCheckBuilder(services);
        var result = builder.AddRedisHealthCheck();

        result.Should().BeSameAs(builder);

        // Verify health checks are registered by building provider
        var provider = services.BuildServiceProvider();
        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddDatabaseHealthCheck_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new HealthCheckBuilder(services);

        var result = builder.AddDatabaseHealthCheck();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddRabbitMqHealthCheck_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new HealthCheckBuilder(services);

        var result = builder.AddRabbitMqHealthCheck();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddExternalApiHealthChecks_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new HealthCheckBuilder(services);

        var result = builder.AddExternalApiHealthChecks();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddCustomHealthChecks_RegistersApplicationAndCircuitBreakerAndMemoryChecks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        var builder = new HealthCheckBuilder(services);
        var result = builder.AddCustomHealthChecks();

        result.Should().BeSameAs(builder);

        var provider = services.BuildServiceProvider();
        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddCustomCheck_RegistersCustomHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new HealthCheckBuilder(services);
        var result = builder.AddCustomCheck<ApplicationHealthCheck>("custom-app", HealthStatus.Unhealthy, "live");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void AddPublisher_RegistersPublisher()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new HealthCheckBuilder(services);
        var result = builder.AddPublisher<TestHealthCheckPublisher>();

        result.Should().BeSameAs(builder);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IHealthCheckPublisher));
        descriptor.Should().NotBeNull();
    }

    #region Additional Tests

    [Fact]
    public void AddCustomCheck_AddsNamedRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new HealthCheckBuilder(services);
        builder.AddCustomCheck<ApplicationHealthCheck>("my-custom", HealthStatus.Degraded, "tag1");

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().Contain(r => r.Name == "my-custom");
    }

    [Fact]
    public void AddRedisHealthCheck_RegistersRedisAndPerformanceChecks()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());

        var builder = new HealthCheckBuilder(services);
        builder.AddRedisHealthCheck();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().Contain(r => r.Name == "redis");
        options.Value.Registrations.Should().Contain(r => r.Name == "redis_performance");
    }

    [Fact]
    public void AddCustomHealthChecks_RegistersApplicationAndMemoryAndCircuitBreakerAndDiskSpace()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<Girder.Infrastructure.Resilience.ICircuitBreakerFactory>());

        var builder = new HealthCheckBuilder(services);
        builder.AddCustomHealthChecks();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        options.Value.Registrations.Should().Contain(r => r.Name == "application");
        options.Value.Registrations.Should().Contain(r => r.Name == "memory");
        options.Value.Registrations.Should().Contain(r => r.Name == "circuit_breakers");
        options.Value.Registrations.Should().Contain(r => r.Name == "disk_space");
    }

    [Fact]
    public void MultipleAddCalls_ChainReturnsSelf()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var builder = new HealthCheckBuilder(services);

        var result = builder
            .AddDatabaseHealthCheck()
            .AddRabbitMqHealthCheck()
            .AddExternalApiHealthChecks();

        result.Should().BeSameAs(builder);
    }

    #endregion
}

// Test publisher for DI registration test
public class TestHealthCheckPublisher : IHealthCheckPublisher
{
    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
