using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Girder.Infrastructure.Observability;

/// <summary>
/// Extension methods for configuring OpenTelemetry
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Add OpenTelemetry tracing and metrics to the service collection
    /// </summary>
    public static IServiceCollection AddTelemetry(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion,
        Action<TelemetryBuilder>? configure = null)
    {
        var builder = new TelemetryBuilder(services);

        // Configure default telemetry settings
        builder.ConfigureResource(serviceName, serviceVersion);

        // Allow custom configuration
        configure?.Invoke(builder);

        // Register telemetry services
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<ICustomMetrics, CustomMetrics>();

        return services;
    }
}

/// <summary>
/// Builder for configuring telemetry
/// </summary>
public class TelemetryBuilder
{
    private readonly IServiceCollection _services;
    private string _serviceName = "GirderService";
    private string _serviceVersion = "1.0.0";
    private ObservabilityOptions _observabilityOptions = new();

    public TelemetryBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configure the service resource information
    /// </summary>
    public TelemetryBuilder ConfigureResource(string serviceName, string serviceVersion)
    {
        _serviceName = serviceName;
        _serviceVersion = serviceVersion;
        return this;
    }

    /// <summary>
    /// Configure runtime observability behavior from appsettings/environment.
    /// </summary>
    public TelemetryBuilder ConfigureObservability(ObservabilityOptions options)
    {
        _observabilityOptions = options ?? new ObservabilityOptions();
        return this;
    }

    /// <summary>
    /// Add distributed tracing
    /// </summary>
    public TelemetryBuilder AddTracing(Action<TracerProviderBuilder>? configure = null)
    {
        _services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                builder
                    .SetResourceBuilder(CreateResourceBuilder())
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            activity.SetTag("http.user_agent", request.Headers.UserAgent.ToString());
                            activity.SetTag("http.client_ip", GetClientIpAddress(request));
                        };
                        options.EnrichWithHttpResponse = (activity, response) =>
                        {
                            activity.SetTag("http.response.content_length", response.ContentLength);
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            activity.SetTag("http.request.content_length", request.Content?.Headers.ContentLength);
                        };
                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                        {
                            activity.SetTag("http.response.content_length", response.Content.Headers.ContentLength);
                        };
                    })
                    .AddEntityFrameworkCoreInstrumentation(options =>
                    {
                        // The instrumentation exposes no public option for statement
                        // capture, so the setting is enforced by removing the tag.
                        if (!_observabilityOptions.CaptureDatabaseStatements)
                        {
                            options.EnrichWithIDbCommand = (activity, _) =>
                            {
                                activity.SetTag("db.statement", null);
                                activity.SetTag("db.query.text", null);
                            };
                        }
                    })
                    .AddRedisInstrumentation()
                    .AddSource(TelemetryConstants.SourceName)
                    .AddOtlpExporter(); // For production

                if (_observabilityOptions.EnableConsoleExporter)
                {
                    builder.AddConsoleExporter();
                }

                configure?.Invoke(builder);
            });

        return this;
    }

    /// <summary>
    /// Add metrics collection
    /// </summary>
    public TelemetryBuilder AddMetrics(Action<MeterProviderBuilder>? configure = null)
    {
        _services.AddOpenTelemetry()
            .WithMetrics(builder =>
            {
                builder
                    .SetResourceBuilder(CreateResourceBuilder())
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddMeter(TelemetryConstants.MeterName)
                    .AddPrometheusExporter(); // For production

                if (_observabilityOptions.EnableConsoleExporter)
                {
                    builder.AddConsoleExporter();
                }

                configure?.Invoke(builder);
            });

        return this;
    }

    /// <summary>
    /// Add logging integration
    /// </summary>
    public TelemetryBuilder AddLogging()
    {
        _services.AddLogging(builder =>
        {
            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(CreateResourceBuilder());
                // options.AddConsoleExporter();
            });
        });

        return this;
    }

    private ResourceBuilder CreateResourceBuilder()
    {
        return ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: _serviceName,
                serviceVersion: _serviceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new[]
            {
                new KeyValuePair<string, object>("deployment.environment", GetEnvironment()),
                new KeyValuePair<string, object>("host.name", Environment.MachineName),
                new KeyValuePair<string, object>("os.type", Environment.OSVersion.Platform.ToString()),
                new KeyValuePair<string, object>("runtime.name", ".NET"),
                new KeyValuePair<string, object>("runtime.version", Environment.Version.ToString())
            });
    }

    private static string GetEnvironment()
    {
        return Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    }

    private static string? GetClientIpAddress(HttpRequest request)
    {
        return request.Headers["X-Forwarded-For"].FirstOrDefault()
               ?? request.Headers["X-Real-IP"].FirstOrDefault()
               ?? request.HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}

/// <summary>
/// Constants for telemetry configuration
/// </summary>
public static class TelemetryConstants
{
    public const string SourceName = "Girder";
    public const string MeterName = "Girder.Metrics";
}

/// <summary>
/// Interface for telemetry service
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Start a new activity/span
    /// </summary>
    Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal);

    /// <summary>
    /// Add tags to the current activity
    /// </summary>
    void AddTags(params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Add an event to the current activity
    /// </summary>
    void AddEvent(string name, params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Record an exception in the current activity
    /// </summary>
    void RecordException(Exception exception);

    /// <summary>
    /// Set the status of the current activity
    /// </summary>
    void SetStatus(ActivityStatusCode statusCode, string? description = null);

    /// <summary>
    /// Create a child span from the current activity
    /// </summary>
    Activity? CreateChildSpan(string name, ActivityKind kind = ActivityKind.Internal);
}

/// <summary>
/// Telemetry service implementation
/// </summary>
public class TelemetryService : ITelemetryService
{
    private static readonly ActivitySource ActivitySource = new(TelemetryConstants.SourceName);

    public Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind);
    }

    public void AddTags(params KeyValuePair<string, object?>[] tags)
    {
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            foreach (var tag in tags)
            {
                currentActivity.SetTag(tag.Key, tag.Value);
            }
        }
    }

    public void AddEvent(string name, params KeyValuePair<string, object?>[] tags)
    {
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            var activityEvent = new ActivityEvent(name, DateTimeOffset.UtcNow, new ActivityTagsCollection(tags));
            currentActivity.AddEvent(activityEvent);
        }
    }

    public void RecordException(Exception exception)
    {
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            // currentActivity.RecordException(exception);
            currentActivity.SetStatus(ActivityStatusCode.Error, exception.Message);
        }
    }

    public void SetStatus(ActivityStatusCode statusCode, string? description = null)
    {
        var currentActivity = Activity.Current;
        if (currentActivity != null)
        {
            currentActivity.SetStatus(statusCode, description);
        }
    }

    public Activity? CreateChildSpan(string name, ActivityKind kind = ActivityKind.Internal)
    {
        var parentContext = Activity.Current?.Context ?? default;
        return ActivitySource.StartActivity(name, kind, parentContext);
    }
}

/// <summary>
/// Interface for custom metrics
/// </summary>
public interface ICustomMetrics
{
    /// <summary>
    /// Record a counter metric
    /// </summary>
    void IncrementCounter(string name, long value = 1, params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Record a histogram metric
    /// </summary>
    void RecordHistogram(string name, double value, params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Record a gauge metric
    /// </summary>
    void RecordGauge(string name, long value, params KeyValuePair<string, object?>[] tags);

    /// <summary>
    /// Start timing an operation
    /// </summary>
    IDisposable StartTimer(string name, params KeyValuePair<string, object?>[] tags);
}

/// <summary>
/// Custom metrics implementation
/// </summary>
public class CustomMetrics : ICustomMetrics
{
    private static readonly Meter Meter = new(TelemetryConstants.MeterName);


    // Infrastructure metrics
    private static readonly Counter<long> CacheHits = Meter.CreateCounter<long>(
        "girder.cache.hits",
        "hits",
        "Number of cache hits");

    private static readonly Counter<long> CacheMisses = Meter.CreateCounter<long>(
        "girder.cache.misses",
        "misses",
        "Number of cache misses");

    private static readonly Histogram<double> RequestDuration = Meter.CreateHistogram<double>(
        "girder.request.duration",
        "milliseconds",
        "Request duration in milliseconds");

    private static readonly Histogram<double> DatabaseQueryDuration = Meter.CreateHistogram<double>(
        "girder.database.query.duration",
        "milliseconds",
        "Database query duration in milliseconds");

    // Rate limiting metrics
    private static readonly Counter<long> RateLimitExceeded = Meter.CreateCounter<long>(
        "girder.rate_limit.exceeded",
        "requests",
        "Number of requests that exceeded rate limits");

    // Circuit breaker metrics
    private static readonly Counter<long> CircuitBreakerOpened = Meter.CreateCounter<long>(
        "girder.circuit_breaker.opened",
        "events",
        "Number of times circuit breaker opened");

    private static readonly Counter<long> CircuitBreakerClosed = Meter.CreateCounter<long>(
        "girder.circuit_breaker.closed",
        "events",
        "Number of times circuit breaker closed");

    public void IncrementCounter(string name, long value = 1, params KeyValuePair<string, object?>[] tags)
    {
        var counter = GetOrCreateCounter(name);
        counter.Add(value, tags);
    }

    public void RecordHistogram(string name, double value, params KeyValuePair<string, object?>[] tags)
    {
        var histogram = GetOrCreateHistogram(name);
        histogram.Record(value, tags);
    }

    public void RecordGauge(string name, long value, params KeyValuePair<string, object?>[] tags)
    {
        // Note: OpenTelemetry .NET doesn't have built-in gauge support yet
        // This is a placeholder for when it becomes available
        // For now, we can use a histogram or counter as a workaround
        RecordHistogram(name, value, tags);
    }

    public IDisposable StartTimer(string name, params KeyValuePair<string, object?>[] tags)
    {
        return new TimerScope(name, tags, this);
    }

    // Business events belong to the application, which records them through
    // IncrementCounter with its own names. A library cannot know what they are.

    // Infrastructure metric helpers
    public void RecordCacheHit(string cacheType = "redis") =>
        CacheHits.Add(1, new KeyValuePair<string, object?>("type", cacheType));

    public void RecordCacheMiss(string cacheType = "redis") =>
        CacheMisses.Add(1, new KeyValuePair<string, object?>("type", cacheType));

    public void RecordRequestDuration(double durationMs, string endpoint, string method) =>
        RequestDuration.Record(durationMs,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("method", method));

    public void RecordDatabaseQueryDuration(double durationMs, string operation) =>
        DatabaseQueryDuration.Record(durationMs,
            new KeyValuePair<string, object?>("operation", operation));

    public void RecordRateLimitExceeded(string clientType, string endpoint) =>
        RateLimitExceeded.Add(1,
            new KeyValuePair<string, object?>("client_type", clientType),
            new KeyValuePair<string, object?>("endpoint", endpoint));

    public void RecordCircuitBreakerOpened(string circuitBreakerName) =>
        CircuitBreakerOpened.Add(1,
            new KeyValuePair<string, object?>("circuit_breaker", circuitBreakerName));

    public void RecordCircuitBreakerClosed(string circuitBreakerName) =>
        CircuitBreakerClosed.Add(1,
            new KeyValuePair<string, object?>("circuit_breaker", circuitBreakerName));

    private Counter<long> GetOrCreateCounter(string name)
    {
        return Meter.CreateCounter<long>(name);
    }

    private Histogram<double> GetOrCreateHistogram(string name)
    {
        return Meter.CreateHistogram<double>(name);
    }

    /// <summary>
    /// Timer scope for measuring operation duration
    /// </summary>
    private class TimerScope : IDisposable
    {
        private readonly string _name;
        private readonly KeyValuePair<string, object?>[] _tags;
        private readonly CustomMetrics _metrics;
        private readonly Stopwatch _stopwatch;

        public TimerScope(string name, KeyValuePair<string, object?>[] tags, CustomMetrics metrics)
        {
            _name = name;
            _tags = tags;
            _metrics = metrics;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _metrics.RecordHistogram(_name, _stopwatch.Elapsed.TotalMilliseconds, _tags);
        }
    }
}
