using System.Reflection;
using Girder.Infrastructure.Communication;
using Girder.Infrastructure.Communication.Deduplication;
using Girder.Infrastructure.Communication.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using CommConfig = Girder.Infrastructure.Communication.Configuration;

namespace Girder.Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class ServiceCommunicationExtensionsTests
{
    #region AddServiceCommunication DI Registration

    [Fact]
    public void AddServiceCommunication_ShouldRegisterServiceCommunicationManager()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddServiceCommunication(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IServiceCommunicationManager) &&
            d.ImplementationType == typeof(ServiceCommunicationManager));
    }

    [Fact]
    public void AddServiceCommunication_ShouldConfigureOptions()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:UseGateway"] = "false",
            ["ServiceCommunication:DefaultTimeout"] = "00:00:15",
        });

        services.AddServiceCommunication(config);
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CommConfig.ServiceCommunicationOptions>>().Value;

        options.UseGateway.Should().BeFalse();
        options.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AddServiceCommunication_WithCachingEnabled_ShouldRegisterCache()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:EnableResponseCaching"] = "true"
        });

        services.AddServiceCommunication(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(Girder.Infrastructure.Communication.Caching.IServiceResponseCache));
    }

    [Fact]
    public void AddServiceCommunication_WithCachingDisabled_ShouldNotRegisterCache()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:EnableResponseCaching"] = "false"
        });

        services.AddServiceCommunication(config);

        services.Should().NotContain(d =>
            d.ServiceType == typeof(Girder.Infrastructure.Communication.Caching.IServiceResponseCache));
    }

    [Fact]
    public void AddServiceCommunication_WithMetricsEnabled_ShouldRegisterMetrics()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:EnableMetrics"] = "true"
        });

        services.AddServiceCommunication(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IServiceCommunicationMetrics));
    }

    [Fact]
    public void AddServiceCommunication_WithMetricsDisabled_ShouldNotRegisterMetrics()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:EnableMetrics"] = "false"
        });

        services.AddServiceCommunication(config);

        services.Should().NotContain(d =>
            d.ServiceType == typeof(IServiceCommunicationMetrics));
    }

    [Fact]
    public void AddServiceCommunication_WithDeduplicationEnabled_ShouldRegisterDeduplicator()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:EnableRequestDeduplication"] = "true"
        });

        services.AddServiceCommunication(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IRequestDeduplicator));
    }

    [Fact]
    public void AddServiceCommunication_WithDeduplicationDisabled_ShouldNotRegisterDeduplicator()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:EnableRequestDeduplication"] = "false"
        });

        services.AddServiceCommunication(config);

        services.Should().NotContain(d =>
            d.ServiceType == typeof(IRequestDeduplicator));
    }

    [Fact]
    public void AddServiceCommunication_WithM2MEnabled_ShouldRegisterTokenProvider()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:M2M:Enabled"] = "true"
        });

        services.AddServiceCommunication(config);

        // M2M token provider registered via AddHttpClient
        services.Should().Contain(d =>
            d.ServiceType == typeof(Girder.Infrastructure.Security.M2M.IServiceTokenProvider));
    }

    [Fact]
    public void AddServiceCommunication_WithM2MDisabled_ShouldNotRegisterTokenProvider()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ServiceCommunication:M2M:Enabled"] = "false"
        });

        services.AddServiceCommunication(config);

        services.Should().NotContain(d =>
            d.ServiceType == typeof(Girder.Infrastructure.Security.M2M.IServiceTokenProvider));
    }

    [Fact]
    public void AddServiceCommunication_ShouldConfigureNamedCircuitBreakerOptions()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddServiceCommunication(config);

        // AddCircuitBreaker("ServiceCommunication", ...) registers IConfigureOptions<CircuitBreakerOptions>
        services.Should().Contain(d =>
            d.ServiceType == typeof(IConfigureOptions<Girder.Infrastructure.Resilience.CircuitBreakerOptions>));
    }

    [Fact]
    public void AddServiceCommunication_ShouldConfigureNamedRetryPolicyOptions()
    {
        var services = new ServiceCollection();
        AddRequiredDependencies(services);
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddServiceCommunication(config);

        // AddRetryPolicy("ServiceCommunication", ...) registers IConfigureOptions<RetryPolicyOptions>
        services.Should().Contain(d =>
            d.ServiceType == typeof(IConfigureOptions<Girder.Infrastructure.Resilience.RetryPolicyOptions>));
    }

    #endregion

    #region MapBackoffStrategy

    [Fact]
    public void MapBackoffStrategy_Linear_ShouldMapToLinear()
    {
        var result = InvokeMapBackoffStrategy(CommConfig.BackoffStrategy.Linear);
        result.Should().Be(Girder.Infrastructure.Resilience.BackoffStrategy.Linear);
    }

    [Fact]
    public void MapBackoffStrategy_ExponentialBackoff_ShouldMapToExponential()
    {
        var result = InvokeMapBackoffStrategy(CommConfig.BackoffStrategy.ExponentialBackoff);
        result.Should().Be(Girder.Infrastructure.Resilience.BackoffStrategy.Exponential);
    }

    [Fact]
    public void MapBackoffStrategy_ExponentialWithJitter_ShouldMapToExponentialWithJitter()
    {
        var result = InvokeMapBackoffStrategy(CommConfig.BackoffStrategy.ExponentialBackoffWithJitter);
        result.Should().Be(Girder.Infrastructure.Resilience.BackoffStrategy.ExponentialWithJitter);
    }

    [Fact]
    public void MapBackoffStrategy_Fibonacci_ShouldFallbackToExponential()
    {
        var result = InvokeMapBackoffStrategy(CommConfig.BackoffStrategy.Fibonacci);
        result.Should().Be(Girder.Infrastructure.Resilience.BackoffStrategy.Exponential);
    }

    #endregion

    #region ShouldRetryException

    [Fact]
    public void ShouldRetryException_HttpRequestException_ShouldReturnTrue()
    {
        var config = new CommConfig.RetryConfiguration();
        var result = InvokeShouldRetryException(new HttpRequestException("fail"), config);
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryException_TimeoutException_WithRetryOnTimeout_ShouldReturnTrue()
    {
        var config = new CommConfig.RetryConfiguration { RetryOnTimeout = true };
        var result = InvokeShouldRetryException(new TimeoutException(), config);
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryException_TimeoutException_WithoutRetryOnTimeout_ShouldStillReturnTrue()
    {
        // TimeoutException is in RetryableExceptions by default
        var config = new CommConfig.RetryConfiguration { RetryOnTimeout = false };
        var result = InvokeShouldRetryException(new TimeoutException(), config);
        result.Should().BeTrue("TimeoutException is in default RetryableExceptions");
    }

    [Fact]
    public void ShouldRetryException_ArgumentException_NotInRetryableExceptions_ShouldReturnFalse()
    {
        var config = new CommConfig.RetryConfiguration
        {
            RetryableExceptions = new HashSet<string> { "HttpRequestException" },
            RetryOnTimeout = false
        };
        var result = InvokeShouldRetryException(new ArgumentException("bad"), config);
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetryException_HttpRequestException_WithRetryableStatusCode_ShouldReturnTrue()
    {
        var config = new CommConfig.RetryConfiguration();
        var ex = new HttpRequestException("fail", null, System.Net.HttpStatusCode.ServiceUnavailable);
        var result = InvokeShouldRetryException(ex, config);
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryException_HttpRequestException_WithNonRetryableStatusCode_ShouldStillReturnTrue()
    {
        // HttpRequestException is in RetryableExceptions by default, so it matches the type name check first
        var config = new CommConfig.RetryConfiguration();
        var ex = new HttpRequestException("fail", null, System.Net.HttpStatusCode.BadRequest);
        var result = InvokeShouldRetryException(ex, config);
        result.Should().BeTrue("HttpRequestException type name is in default RetryableExceptions");
    }

    #endregion

    #region RetryConfiguration Defaults

    [Fact]
    public void RetryConfiguration_Defaults_ShouldBeCorrect()
    {
        var config = new CommConfig.RetryConfiguration();

        config.MaxRetries.Should().Be(3);
        config.BackoffStrategy.Should().Be(CommConfig.BackoffStrategy.ExponentialBackoff);
        config.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        config.MaxDelay.Should().Be(TimeSpan.FromSeconds(10));
        config.RetryOnTimeout.Should().BeTrue();
        config.UseJitter.Should().BeTrue();
        config.JitterFactor.Should().Be(0.2);
    }

    [Fact]
    public void RetryConfiguration_DefaultRetryableStatusCodes_ShouldContainExpected()
    {
        var config = new CommConfig.RetryConfiguration();

        config.RetryableStatusCodes.Should().Contain(408);
        config.RetryableStatusCodes.Should().Contain(429);
        config.RetryableStatusCodes.Should().Contain(500);
        config.RetryableStatusCodes.Should().Contain(502);
        config.RetryableStatusCodes.Should().Contain(503);
        config.RetryableStatusCodes.Should().Contain(504);
    }

    [Fact]
    public void RetryConfiguration_DefaultRetryableExceptions_ShouldContainExpected()
    {
        var config = new CommConfig.RetryConfiguration();

        config.RetryableExceptions.Should().Contain("TimeoutException");
        config.RetryableExceptions.Should().Contain("HttpRequestException");
        config.RetryableExceptions.Should().Contain("TaskCanceledException");
        config.RetryableExceptions.Should().Contain("OperationCanceledException");
    }

    #endregion

    #region M2MConfiguration Defaults

    [Fact]
    public void M2MConfiguration_Defaults_ShouldBeCorrect()
    {
        var config = new CommConfig.M2MConfiguration();

        config.Enabled.Should().BeTrue();
        config.TokenEndpoint.Should().BeNull();
        config.ClientId.Should().BeNull();
        config.ClientSecret.Should().BeNull();
        config.TokenLifetime.Should().Be(TimeSpan.FromHours(1));
        config.RefreshBeforeExpiry.Should().Be(TimeSpan.FromMinutes(5));
        config.EnableTokenCaching.Should().BeTrue();
        config.UseMutualTLS.Should().BeFalse();
    }

    #endregion

    #region ServiceCommunicationOptions Defaults

    [Fact]
    public void ServiceCommunicationOptions_Defaults_ShouldBeCorrect()
    {
        var opts = new CommConfig.ServiceCommunicationOptions();

        opts.UseGateway.Should().BeTrue();
        opts.DefaultTimeout.Should().Be(TimeSpan.FromSeconds(30));
        opts.EnableResponseCaching.Should().BeTrue();
        opts.EnableMetrics.Should().BeTrue();
        opts.EnableRequestDeduplication.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static void AddRequiredDependencies(IServiceCollection services)
    {
        services.AddLogging();
        services.AddHttpContextAccessor();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Girder.Infrastructure.Resilience.BackoffStrategy InvokeMapBackoffStrategy(CommConfig.BackoffStrategy strategy)
    {
        var method = typeof(ServiceCommunicationExtensions)
            .GetMethod("MapBackoffStrategy", BindingFlags.NonPublic | BindingFlags.Static);
        return (Girder.Infrastructure.Resilience.BackoffStrategy)method!.Invoke(null, [strategy])!;
    }

    private static bool InvokeShouldRetryException(Exception ex, CommConfig.RetryConfiguration retryConfig)
    {
        var method = typeof(ServiceCommunicationExtensions)
            .GetMethod("ShouldRetryException", BindingFlags.NonPublic | BindingFlags.Static);
        return (bool)method!.Invoke(null, [ex, retryConfig])!;
    }

    #endregion
}
