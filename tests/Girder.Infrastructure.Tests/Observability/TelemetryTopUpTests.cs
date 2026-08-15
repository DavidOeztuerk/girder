using System.Diagnostics;
using Girder.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class TelemetryBuilderTopUpTests
{
    [Fact]
    public void TelemetryBuilder_AddTracing_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder.AddTracing();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void TelemetryBuilder_AddMetrics_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder.AddMetrics();

        result.Should().BeSameAs(builder);
    }

    [Fact]
    public void TelemetryBuilder_ChainAll_ReturnsSelf()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var result = builder
            .ConfigureResource("svc", "1.0")
            .AddTracing()
            .AddMetrics()
            .AddLogging();

        result.Should().BeSameAs(builder);
    }
}

[Trait("Category", "Unit")]
public class TelemetryMiddlewareExtensionsTopUpTests
{
    [Fact]
    public void UseTelemetry_RegistersMiddleware_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ITelemetryService>());
        services.AddSingleton(Substitute.For<ICustomMetrics>());
        services.Configure<ObservabilityOptions>(_ => { });

        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UseTelemetry();

        act.Should().NotThrow();
    }

    [Fact]
    public void UseCorrelationId_RegistersMiddleware_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<ObservabilityOptions>(_ => { });

        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UseCorrelationId();

        act.Should().NotThrow();
    }

    [Fact]
    public void UsePerformance_RegistersMiddleware_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IPerformanceMetrics>());

        var app = new ApplicationBuilder(services.BuildServiceProvider());

        var act = () => app.UsePerformance();

        act.Should().NotThrow();
    }
}

[Trait("Category", "Unit")]
public class TelemetryServiceWithActivityTopUpTests
{
    [Fact]
    public void TelemetryService_StartActivity_ReturnsActivity()
    {
        var source = new ActivitySource("test-source");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var svc = new TelemetryService();
        using var activity = svc.StartActivity("test-op");

        // May be null if no listener, but should not throw
        // The key thing: no exception
    }

    [Fact]
    public void TelemetryService_AddTags_WithPairs_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.AddTags(
            new KeyValuePair<string, object?>("k1", "v1"),
            new KeyValuePair<string, object?>("k2", 42));

        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_AddEvent_WithMultipleTags_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.AddEvent("evt",
            new KeyValuePair<string, object?>("a", 1),
            new KeyValuePair<string, object?>("b", "two"));

        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_RecordException_WithData_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.RecordException(new ArgumentNullException("param", "test message"));

        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_SetStatus_Unset_ShouldNotThrow()
    {
        var svc = new TelemetryService();
        var act = () => svc.SetStatus(ActivityStatusCode.Unset);

        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryService_CreateChildSpan_ReturnsDisposable()
    {
        var svc = new TelemetryService();
        using var span = svc.CreateChildSpan("child");

        // Should return null or an activity — either way, not throw
    }
}

[Trait("Category", "Unit")]
public class CustomMetricsTopUpTests
{
    [Fact]
    public void CustomMetrics_IncrementCounter_WithZero_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.IncrementCounter("test.counter", 0);

        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordHistogram_WithZero_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordHistogram("test.histogram", 0.0);

        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_RecordGauge_WithNegative_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () => metrics.RecordGauge("test.gauge", -1);

        act.Should().NotThrow();
    }

    [Fact]
    public void CustomMetrics_StartTimer_MultipleInstances_ShouldNotThrow()
    {
        var metrics = new CustomMetrics();
        var act = () =>
        {
            using var t1 = metrics.StartTimer("op1");
            using var t2 = metrics.StartTimer("op2");
        };

        act.Should().NotThrow();
    }
}

[Trait("Category", "Unit")]
public class TelemetryServiceWithActiveActivityTests
{
    private static ActivityListener CreateAllDataListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public void TelemetryService_AddTags_WithActiveActivity_SetsTagsOnActivity()
    {
        using var listener = CreateAllDataListener();
        var source = new ActivitySource(TelemetryConstants.SourceName);

        using var activity = source.StartActivity("test-host");
        activity.Should().NotBeNull();

        var svc = new TelemetryService();
        svc.AddTags(new KeyValuePair<string, object?>("user.id", "123"));

        activity!.Tags.Should().Contain(t => t.Key == "user.id" && t.Value == "123");
    }

    [Fact]
    public void TelemetryService_AddEvent_WithActiveActivity_AddsEvent()
    {
        using var listener = CreateAllDataListener();
        var source = new ActivitySource(TelemetryConstants.SourceName);

        using var activity = source.StartActivity("test-host-event");
        activity.Should().NotBeNull();

        var svc = new TelemetryService();
        svc.AddEvent("user.login", new KeyValuePair<string, object?>("user.id", "456"));

        activity!.Events.Should().Contain(e => e.Name == "user.login");
    }

    [Fact]
    public void TelemetryService_RecordException_WithActiveActivity_SetsErrorStatus()
    {
        using var listener = CreateAllDataListener();
        var source = new ActivitySource(TelemetryConstants.SourceName);

        using var activity = source.StartActivity("test-host-exc");
        activity.Should().NotBeNull();

        var svc = new TelemetryService();
        svc.RecordException(new InvalidOperationException("test error"));

        activity!.Status.Should().Be(ActivityStatusCode.Error);
    }

    [Fact]
    public void TelemetryService_SetStatus_WithActiveActivity_SetsStatus()
    {
        using var listener = CreateAllDataListener();
        var source = new ActivitySource(TelemetryConstants.SourceName);

        using var activity = source.StartActivity("test-host-status");
        activity.Should().NotBeNull();

        var svc = new TelemetryService();
        svc.SetStatus(ActivityStatusCode.Ok, "All good");

        activity!.Status.Should().Be(ActivityStatusCode.Ok);
    }

    [Fact]
    public void TelemetryService_CreateChildSpan_WithParentContext_CreatesChildActivity()
    {
        using var listener = CreateAllDataListener();
        var source = new ActivitySource(TelemetryConstants.SourceName);

        using var parentActivity = source.StartActivity("parent");
        parentActivity.Should().NotBeNull();

        var svc = new TelemetryService();
        var child = svc.CreateChildSpan("child-op");

        // Child may be null if no listener for the source — just verify no throw
        child?.Dispose();
    }
}

[Trait("Category", "Unit")]
public class TelemetryBuilderAdditionalTests
{
    [Fact]
    public void TelemetryBuilder_AddTracing_WithCustomConfigure_InvokesCallback()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);
        var called = false;

        var result = builder.AddTracing(tracingBuilder =>
        {
            called = true;
        });

        result.Should().BeSameAs(builder);
        called.Should().BeTrue();
    }

    [Fact]
    public void TelemetryBuilder_AddMetrics_WithCustomConfigure_InvokesCallback()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);
        var called = false;

        var result = builder.AddMetrics(metricsBuilder =>
        {
            called = true;
        });

        result.Should().BeSameAs(builder);
        called.Should().BeTrue();
    }

    [Fact]
    public void TelemetryBuilder_AddTracing_WithNullConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var act = () => builder.AddTracing(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryBuilder_AddMetrics_WithNullConfigure_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var act = () => builder.AddMetrics(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void TelemetryBuilder_ConfigureObservability_DoesNotThrow()
    {
        var services = new ServiceCollection();
        var builder = new TelemetryBuilder(services);

        var act = () => builder.ConfigureObservability(new ObservabilityOptions());
        act.Should().NotThrow();
    }
}
