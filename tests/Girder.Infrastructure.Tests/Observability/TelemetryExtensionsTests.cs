using System.Diagnostics;
using Girder.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class TelemetryExtensionsStaticTests
{
    [Fact]
    public void AddTelemetry_RegistersTelemetryService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTelemetry("TestService", "1.0.0");

        services.Should().Contain(d => d.ServiceType == typeof(ITelemetryService));
    }

    [Fact]
    public void AddTelemetry_RegistersCustomMetrics()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddTelemetry("TestService", "1.0.0");

        services.Should().Contain(d => d.ServiceType == typeof(ICustomMetrics));
    }

    [Fact]
    public void AddTelemetry_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddTelemetry("TestService", "1.0.0");

        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddTelemetry_WithConfigureCallback_InvokesBuilder()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var called = false;

        services.AddTelemetry("TestService", "1.0.0", builder =>
        {
            called = true;
            builder.ConfigureResource("TestService", "2.0.0");
        });

        called.Should().BeTrue();
    }

    [Fact]
    public void AddTelemetry_WithNullConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddTelemetry("TestService", "1.0.0", null);

        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryBuilder_ConfigureResource_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder.ConfigureResource("svc", "1.0");

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void TelemetryBuilder_ConfigureObservability_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder.ConfigureObservability(new ObservabilityOptions());

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void TelemetryBuilder_ConfigureObservability_WithNull_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder.ConfigureObservability(null!);

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void TelemetryBuilder_AddLogging_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder.AddLogging();

        result.Should().BeSameAs(builder);
    }
}

[Trait("Category", "Unit")]
public class TelemetryExtensionsTests
{
    [Fact]
    public void TelemetryConstants_SourceName_ShouldBeGirder()
    {
        TelemetryConstants.SourceName.Should().Be("Girder");
    }

    [Fact]
    public void TelemetryConstants_MeterName_ShouldBeGirderMetrics()
    {
        TelemetryConstants.MeterName.Should().Be("Girder.Metrics");
    }

    [Fact]
    public void TelemetryService_StartActivity_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.StartActivity("test-activity");
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_AddTags_WithNoCurrentActivity_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.AddTags(
            new KeyValuePair<string, object?>("key", "value"));
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_AddEvent_WithNoCurrentActivity_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.AddEvent("test-event",
            new KeyValuePair<string, object?>("key", "value"));
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_RecordException_WithNoCurrentActivity_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.RecordException(new System.InvalidOperationException("test"));
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_SetStatus_WithNoCurrentActivity_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.SetStatus(ActivityStatusCode.Ok);
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_SetStatus_Error_WithNoCurrentActivity_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.SetStatus(ActivityStatusCode.Error, "error description");
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_CreateChildSpan_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.CreateChildSpan("child-span");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_IncrementCounter_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.IncrementCounter("test.counter", 1);
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordHistogram_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordHistogram("test.histogram", 42.0);
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordGauge_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordGauge("test.gauge", 100);
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_StartTimer_ShouldReturnDisposable()
    {
        var metrics = new CustomMetrics();
        using var timer = metrics.StartTimer("test.timer");
        timer.Should().NotBeNull();
    }

    [Fact]
    public void CustomMetrics_StartTimer_Dispose_ShouldRecordHistogram()
    {
        var metrics = new CustomMetrics();
        var timer = metrics.StartTimer("test.timer");
        var act = () => timer.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordCacheHit_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordCacheHit("redis");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordCacheMiss_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordCacheMiss("redis");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordRequestDuration_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordRequestDuration(50.0, "/api/test", "GET");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordDatabaseQueryDuration_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordDatabaseQueryDuration(10.0, "SELECT");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordRateLimitExceeded_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordRateLimitExceeded("ip", "/api/test");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordCircuitBreakerOpened_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordCircuitBreakerOpened("UserService");
        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordCircuitBreakerClosed_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordCircuitBreakerClosed("UserService");
        act.Should().NotThrow();
    }
}
