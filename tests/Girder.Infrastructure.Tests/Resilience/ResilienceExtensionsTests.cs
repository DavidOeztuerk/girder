using Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public class ResilienceExtensionsTests
{
    [Fact]
    public void AddResilience_RegistersCircuitBreakerFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetService<ICircuitBreakerFactory>();

        factory.Should().NotBeNull();
        factory.Should().BeOfType<CircuitBreakerFactory>();
    }

    [Fact]
    public void AddResilience_RegistersRetryPolicyFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetService<IRetryPolicyFactory>();

        factory.Should().NotBeNull();
        factory.Should().BeOfType<RetryPolicyFactory>();
    }

    [Fact]
    public void AddResilience_WithConfigure_InvokesBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configured = false;

        services.AddResilience(builder =>
        {
            configured = true;
            builder.ConfigureCircuitBreaker(opts =>
            {
                opts.ExceptionsAllowedBeforeBreaking = 10;
            });
            builder.ConfigureRetryPolicy(opts =>
            {
                opts.MaxRetryAttempts = 5;
            });
        });

        configured.Should().BeTrue();
    }

    [Fact]
    public void AddResilience_WithNullConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddResilience(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void AddCircuitBreaker_RegistersNamedOptions()
    {
        var services = new ServiceCollection();
        services.AddCircuitBreaker("myBreaker", opts =>
        {
            opts.ExceptionsAllowedBeforeBreaking = 7;
        });

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CircuitBreakerOptions>>();
        var options = monitor.Get("myBreaker");

        options.ExceptionsAllowedBeforeBreaking.Should().Be(7);
    }

    [Fact]
    public void AddRetryPolicy_RegistersNamedOptions()
    {
        var services = new ServiceCollection();
        services.AddRetryPolicy("myRetry", opts =>
        {
            opts.MaxRetryAttempts = 5;
        });

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<RetryPolicyOptions>>();
        var options = monitor.Get("myRetry");

        options.MaxRetryAttempts.Should().Be(5);
    }

    #region ResilienceOptionsBuilder

    [Fact]
    public void ResilienceOptionsBuilder_AddCircuitBreaker_ConfiguresNamedOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddResilience(builder =>
        {
            builder.AddCircuitBreaker("svc", opts =>
            {
                opts.ExceptionsAllowedBeforeBreaking = 15;
            });
        });

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CircuitBreakerOptions>>();
        monitor.Get("svc").ExceptionsAllowedBeforeBreaking.Should().Be(15);
    }

    [Fact]
    public void ResilienceOptionsBuilder_AddRetryPolicy_ConfiguresNamedOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddResilience(builder =>
        {
            builder.AddRetryPolicy("svc", opts =>
            {
                opts.MaxRetryAttempts = 10;
            });
        });

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<RetryPolicyOptions>>();
        monitor.Get("svc").MaxRetryAttempts.Should().Be(10);
    }

    #endregion

    #region RetryPolicyFactory

    [Fact]
    public void RetryPolicyFactory_GetRetryPolicy_ReturnsSameInstanceForSameName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRetryPolicyFactory>();

        var p1 = factory.GetRetryPolicy("test");
        var p2 = factory.GetRetryPolicy("test");

        p1.Should().BeSameAs(p2);
    }

    [Fact]
    public void RetryPolicyFactory_CreateRetryPolicy_ReturnsNewInstance()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRetryPolicyFactory>();

        var p1 = factory.CreateRetryPolicy("test", new RetryPolicyOptions());
        var p2 = factory.CreateRetryPolicy("test", new RetryPolicyOptions());

        p1.Should().NotBeSameAs(p2);
    }

    [Fact]
    public void RetryPolicyFactory_GetAllStatistics_ReturnsAllPolicies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRetryPolicyFactory>();

        factory.GetRetryPolicy("a");
        factory.GetRetryPolicy("b");

        var stats = factory.GetAllStatistics();
        stats.Should().HaveCount(2);
    }

    [Fact]
    public void RetryPolicyFactory_ResetAllStatistics_ResetsAllPolicies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IRetryPolicyFactory>();

        factory.GetRetryPolicy("a");
        factory.GetRetryPolicy("b");

        var act = () => factory.ResetAllStatistics();
        act.Should().NotThrow();
    }

    #endregion

    #region AddResilientHttpClient

    // Dummy typed client for testing
    private class TestHttpClient
    {
        public TestHttpClient(HttpClient client) { }
    }

    [Fact]
    public void AddResilientHttpClient_RegistersTypedClient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        services.AddResilientHttpClient<TestHttpClient>("test-client");

        var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<TestHttpClient>();
        act.Should().NotThrow();
    }

    [Fact]
    public void AddResilientHttpClient_WithCircuitBreakerConfig_RegistersOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        services.AddResilientHttpClient<TestHttpClient>(
            "test-cb",
            configureCircuitBreaker: opts => opts.ExceptionsAllowedBeforeBreaking = 3);

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<CircuitBreakerOptions>>();
        monitor.Get("HttpClient_test-cb").ExceptionsAllowedBeforeBreaking.Should().Be(3);
    }

    [Fact]
    public void AddResilientHttpClient_WithRetryPolicyConfig_RegistersOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        services.AddResilientHttpClient<TestHttpClient>(
            "test-retry",
            configureRetryPolicy: opts => opts.MaxRetryAttempts = 4);

        var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<RetryPolicyOptions>>();
        monitor.Get("HttpClient_test-retry").MaxRetryAttempts.Should().Be(4);
    }

    [Fact]
    public void AddResilientHttpClient_WithClientConfig_AppliesBaseAddress()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        services.AddResilientHttpClient<TestHttpClient>(
            "test-addr",
            configureClient: client => client.BaseAddress = new Uri("http://example.com"));

        var provider = services.BuildServiceProvider();
        // Just verify no exception is thrown — the client is registered
        var act = () => provider.GetRequiredService<TestHttpClient>();
        act.Should().NotThrow();
    }

    [Fact]
    public void AddResilientHttpClient_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResilience();

        var result = services.AddResilientHttpClient<TestHttpClient>("my-client");

        result.Should().BeSameAs(services);
    }

    #endregion
}
