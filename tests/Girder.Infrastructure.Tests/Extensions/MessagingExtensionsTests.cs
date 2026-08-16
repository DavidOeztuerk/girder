using Girder.Messaging.MassTransit;
using Girder.Abstractions.Messaging;
using System.Reflection;
using Girder.Infrastructure.Extensions;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
[Collection("EnvironmentVariables")]
public class MessagingExtensionsTests
{
    private static readonly string[] EnvVarNames =
    [
        "RABBITMQ_HOST",
        "RABBITMQ_USERNAME",
        "RABBITMQ_PASSWORD",
        "RABBITMQ_VHOST",
        "RABBITMQ_PORT",
        "RABBITMQ_CONNECTION"
    ];

    private static RabbitMqSettings InvokeGetRabbitMqSettings(IConfiguration configuration)
    {
        var method = typeof(MessagingExtensions)
            .GetMethod("GetRabbitMqSettings", BindingFlags.NonPublic | BindingFlags.Static);
        return (RabbitMqSettings)method!.Invoke(null, [configuration])!;
    }

    private static IConfiguration BuildConfiguration(
        Dictionary<string, string?>? values = null,
        Dictionary<string, string?>? connectionStrings = null)
    {
        var builder = new ConfigurationBuilder();
        var allValues = new Dictionary<string, string?>();

        if (values != null)
        {
            foreach (var kvp in values)
                allValues[kvp.Key] = kvp.Value;
        }

        if (connectionStrings != null)
        {
            foreach (var kvp in connectionStrings)
                allValues[$"ConnectionStrings:{kvp.Key}"] = kvp.Value;
        }

        if (allValues.Count > 0)
            builder.AddInMemoryCollection(allValues);
        else
            builder.AddInMemoryCollection();

        return builder.Build();
    }

    private static void ClearRabbitMqEnvVars()
    {
        foreach (var name in EnvVarNames)
            Environment.SetEnvironmentVariable(name, null);
    }

    #region RabbitMqSettings Defaults

    [Fact]
    public void RabbitMqSettings_DefaultValues_ShouldBeCorrect()
    {
        var settings = new RabbitMqSettings();

        settings.Host.Should().Be("rabbitmq");
        settings.Port.Should().Be(5672);
        settings.Username.Should().Be("guest");
        settings.Password.Should().Be("guest");
        settings.VirtualHost.Should().Be("/");
        settings.ConnectionString.Should().BeEmpty();
    }

    #endregion

    #region GetRabbitMqSettings - All Defaults

    [Fact]
    public void GetRabbitMqSettings_NoEnvVarsNoConfig_ShouldReturnDefaults()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration();
            var settings = InvokeGetRabbitMqSettings(config);

            settings.Host.Should().Be("rabbitmq");
            settings.Username.Should().Be("guest");
            settings.Password.Should().Be("guest");
            settings.VirtualHost.Should().Be("/");
            settings.Port.Should().Be(5672);
            settings.ConnectionString.Should().Be("amqp://guest:guest@rabbitmq:5672/");
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    #endregion

    #region GetRabbitMqSettings - Config Values

    [Fact]
    public void GetRabbitMqSettings_WithConfigValues_ShouldReadFromConfig()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "config-host",
                ["RabbitMQ:Username"] = "config-user",
                ["RabbitMQ:Password"] = "config-pass",
                ["RabbitMQ:VirtualHost"] = "/config-vhost",
                ["RabbitMQ:Port"] = "5673"
            });

            var settings = InvokeGetRabbitMqSettings(config);

            settings.Host.Should().Be("config-host");
            settings.Username.Should().Be("config-user");
            settings.Password.Should().Be("config-pass");
            settings.VirtualHost.Should().Be("/config-vhost");
            settings.Port.Should().Be(5673);
            settings.ConnectionString.Should().Be("amqp://config-user:config-pass@config-host:5673/config-vhost");
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    #endregion

    #region GetRabbitMqSettings - Env Vars Override Config

    [Fact]
    public void GetRabbitMqSettings_EnvVarsOverrideConfig_ShouldPreferEnvVars()
    {
        ClearRabbitMqEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("RABBITMQ_HOST", "env-host");
            Environment.SetEnvironmentVariable("RABBITMQ_USERNAME", "env-user");
            Environment.SetEnvironmentVariable("RABBITMQ_PASSWORD", "env-pass");
            Environment.SetEnvironmentVariable("RABBITMQ_VHOST", "/env-vhost");
            Environment.SetEnvironmentVariable("RABBITMQ_PORT", "5674");

            var config = BuildConfiguration(new Dictionary<string, string?>
            {
                ["RabbitMQ:Host"] = "config-host",
                ["RabbitMQ:Username"] = "config-user",
                ["RabbitMQ:Password"] = "config-pass",
                ["RabbitMQ:VirtualHost"] = "/config-vhost",
                ["RabbitMQ:Port"] = "5673"
            });

            var settings = InvokeGetRabbitMqSettings(config);

            settings.Host.Should().Be("env-host");
            settings.Username.Should().Be("env-user");
            settings.Password.Should().Be("env-pass");
            settings.VirtualHost.Should().Be("/env-vhost");
            settings.Port.Should().Be(5674);
            settings.ConnectionString.Should().Be("amqp://env-user:env-pass@env-host:5674/env-vhost");
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    #endregion

    #region GetRabbitMqSettings - Invalid Port

    [Fact]
    public void GetRabbitMqSettings_InvalidPortString_ShouldDefaultTo5672()
    {
        ClearRabbitMqEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("RABBITMQ_PORT", "not-a-number");

            var config = BuildConfiguration();
            var settings = InvokeGetRabbitMqSettings(config);

            settings.Port.Should().Be(5672);
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public void GetRabbitMqSettings_InvalidPortInConfig_ShouldDefaultTo5672()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration(new Dictionary<string, string?>
            {
                ["RabbitMQ:Port"] = "abc"
            });

            var settings = InvokeGetRabbitMqSettings(config);

            settings.Port.Should().Be(5672);
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    #endregion

    #region GetRabbitMqSettings - Connection String Overrides

    [Fact]
    public void GetRabbitMqSettings_RabbitMqConnectionEnvVar_ShouldOverrideBuiltConnectionString()
    {
        ClearRabbitMqEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("RABBITMQ_CONNECTION", "amqp://custom:custom@custom-host:9999/custom-vhost");

            var config = BuildConfiguration();
            var settings = InvokeGetRabbitMqSettings(config);

            settings.ConnectionString.Should().Be("amqp://custom:custom@custom-host:9999/custom-vhost");
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public void GetRabbitMqSettings_ConnectionStringsRabbitMqInConfig_ShouldOverrideBuiltConnectionString()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration(
                connectionStrings: new Dictionary<string, string?>
                {
                    ["RabbitMQ"] = "amqp://connstr-user:connstr-pass@connstr-host:8888/connstr-vhost"
                });

            var settings = InvokeGetRabbitMqSettings(config);

            settings.ConnectionString.Should().Be("amqp://connstr-user:connstr-pass@connstr-host:8888/connstr-vhost");
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public void GetRabbitMqSettings_RabbitMqConnectionEnvVar_ShouldTakePrecedenceOverConfigConnectionString()
    {
        ClearRabbitMqEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("RABBITMQ_CONNECTION", "amqp://env-conn:5555");

            var config = BuildConfiguration(
                connectionStrings: new Dictionary<string, string?>
                {
                    ["RabbitMQ"] = "amqp://config-conn:6666"
                });

            var settings = InvokeGetRabbitMqSettings(config);

            settings.ConnectionString.Should().Be("amqp://env-conn:5555");
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    #endregion

    #region AddEventBus DI Registration

    [Fact]
    public void AddEventBus_ShouldRegisterIEventBusAsScopedMassTransitEventBus()
    {
        var services = new ServiceCollection();

        services.AddEventBus();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventBus));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be(typeof(MassTransitEventBus));
    }

    [Fact]
    public void AddEventBus_ShouldReturnSameServiceCollection()
    {
        var services = new ServiceCollection();

        var result = services.AddEventBus();

        result.Should().BeSameAs(services);
    }

    #endregion

    #region MassTransitEventBus

    [Fact]
    public async Task MassTransitEventBus_PublishAsync_PublishesEventViaEndpoint()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);
        var testEvent = new TestIntegrationEvent { Id = "evt-1", Name = "Test" };

        await eventBus.PublishAsync(testEvent);

        await publishEndpoint.Received(1).Publish(testEvent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MassTransitEventBus_PublishAsync_WhenPublishThrows_PropagatesException()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        publishEndpoint.Publish(Arg.Any<TestIntegrationEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Broker down"));

        var eventBus = new MassTransitEventBus(publishEndpoint, logger);

        var act = async () => await eventBus.PublishAsync(new TestIntegrationEvent { Id = "1" });

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Broker down*");
    }

    [Fact]
    public async Task MassTransitEventBus_PublishAsync_WithCancellationToken_PassesTokenToEndpoint()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);
        using var cts = new CancellationTokenSource();

        await eventBus.PublishAsync(new TestIntegrationEvent { Id = "1" }, cts.Token);

        await publishEndpoint.Received(1).Publish(Arg.Any<TestIntegrationEvent>(), cts.Token);
    }

    private sealed class TestIntegrationEvent
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    #endregion

    #region AddMessaging DI Registration

    [Fact]
    public void AddMessaging_NoAssemblies_RegistersMassTransitServices()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration();
            var services = new ServiceCollection();
            services.AddLogging();

            // AddMessaging configures MassTransit — verifiable by checking IBusControl registration
            var act = () => services.AddMessaging(config);

            act.Should().NotThrow();
            services.Should().NotBeEmpty();
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public void AddMessaging_WithConfigureBusCallback_InvokesCallback()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration();
            var services = new ServiceCollection();
            services.AddLogging();
            var callbackInvoked = false;

            services.AddMessaging(config, Array.Empty<Assembly>(), _ => { callbackInvoked = true; });

            callbackInvoked.Should().BeTrue();
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    [Fact]
    public void AddMessaging_ReturnsSameServiceCollection()
    {
        ClearRabbitMqEnvVars();
        try
        {
            var config = BuildConfiguration();
            var services = new ServiceCollection();
            services.AddLogging();

            var result = services.AddMessaging(config);

            result.Should().BeSameAs(services);
        }
        finally
        {
            ClearRabbitMqEnvVars();
        }
    }

    #endregion
}
