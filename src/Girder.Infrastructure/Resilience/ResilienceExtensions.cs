using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Girder.Infrastructure.Resilience;

/// <summary>
/// Extension methods for configuring resilience patterns
/// </summary>
public static class ResilienceExtensions
{
    /// <summary>
    /// Add resilience services (Circuit Breaker, Retry Policy) to the container
    /// </summary>
    public static IServiceCollection AddResilience(
        this IServiceCollection services,
        Action<ResilienceOptionsBuilder>? configure = null)
    {
        var builder = new ResilienceOptionsBuilder(services);
        configure?.Invoke(builder);

        // Register core resilience services
        services.AddSingleton<ICircuitBreakerFactory, CircuitBreakerFactory>();
        services.AddSingleton<IRetryPolicyFactory, RetryPolicyFactory>();

        return services;
    }

    /// <summary>
    /// Add a named circuit breaker configuration
    /// </summary>
    public static IServiceCollection AddCircuitBreaker(
        this IServiceCollection services,
        string name,
        Action<CircuitBreakerOptions> configure)
    {
        services.Configure<CircuitBreakerOptions>(name, configure);
        return services;
    }

    /// <summary>
    /// Add a named retry policy configuration
    /// </summary>
    public static IServiceCollection AddRetryPolicy(
        this IServiceCollection services,
        string name,
        Action<RetryPolicyOptions> configure)
    {
        services.Configure<RetryPolicyOptions>(name, configure);
        return services;
    }

    /// <summary>
    /// Create a resilient HTTP client with circuit breaker and retry policy
    /// </summary>
    public static IServiceCollection AddResilientHttpClient<TClient>(
        this IServiceCollection services,
        string name,
        Action<HttpClient>? configureClient = null,
        Action<CircuitBreakerOptions>? configureCircuitBreaker = null,
        Action<RetryPolicyOptions>? configureRetryPolicy = null)
        where TClient : class
    {
        // Configure circuit breaker for this client
        if (configureCircuitBreaker != null)
        {
            services.Configure<CircuitBreakerOptions>($"HttpClient_{name}", configureCircuitBreaker);
        }

        // Configure retry policy for this client
        if (configureRetryPolicy != null)
        {
            services.Configure<RetryPolicyOptions>($"HttpClient_{name}", configureRetryPolicy);
        }

        // Register the typed HTTP client
        services.AddHttpClient<TClient>(name, configureClient ?? (_ => { }))
            .AddHttpMessageHandler(serviceProvider =>
            {
                var circuitBreakerFactory = serviceProvider.GetRequiredService<ICircuitBreakerFactory>();
                var retryPolicyFactory = serviceProvider.GetRequiredService<IRetryPolicyFactory>();

                var circuitBreaker = circuitBreakerFactory.GetCircuitBreaker($"HttpClient_{name}");
                var retryPolicy = retryPolicyFactory.GetRetryPolicy($"HttpClient_{name}");

                return new ResilientHttpPolicyHandler(circuitBreaker, retryPolicy);
            });

        return services;
    }
}

/// <summary>
/// Builder for configuring resilience options
/// </summary>
public class ResilienceOptionsBuilder
{
    private readonly IServiceCollection _services;

    public ResilienceOptionsBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    /// Configure default circuit breaker options
    /// </summary>
    public ResilienceOptionsBuilder ConfigureCircuitBreaker(Action<CircuitBreakerOptions> configure)
    {
        _services.Configure<CircuitBreakerOptions>(configure);
        return this;
    }

    /// <summary>
    /// Configure default retry policy options
    /// </summary>
    public ResilienceOptionsBuilder ConfigureRetryPolicy(Action<RetryPolicyOptions> configure)
    {
        _services.Configure<RetryPolicyOptions>(configure);
        return this;
    }

    /// <summary>
    /// Add circuit breaker with specific name and configuration
    /// </summary>
    public ResilienceOptionsBuilder AddCircuitBreaker(string name, Action<CircuitBreakerOptions> configure)
    {
        _services.Configure<CircuitBreakerOptions>(name, configure);
        return this;
    }

    /// <summary>
    /// Add retry policy with specific name and configuration
    /// </summary>
    public ResilienceOptionsBuilder AddRetryPolicy(string name, Action<RetryPolicyOptions> configure)
    {
        _services.Configure<RetryPolicyOptions>(name, configure);
        return this;
    }
}

/// <summary>
/// Factory for creating retry policies
/// </summary>
public class RetryPolicyFactory : IRetryPolicyFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<RetryPolicyOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, IRetryPolicy> _policies = new();

    public RetryPolicyFactory(
        ILoggerFactory loggerFactory,
        IOptionsMonitor<RetryPolicyOptions> optionsMonitor)
    {
        _loggerFactory = loggerFactory;
        _optionsMonitor = optionsMonitor;
    }

    public IRetryPolicy GetRetryPolicy(string name)
    {
        return _policies.GetOrAdd(name, CreateRetryPolicyInternal);
    }

    public IRetryPolicy CreateRetryPolicy(string name, RetryPolicyOptions options)
    {
        var logger = _loggerFactory.CreateLogger<RetryPolicy>();
        return new RetryPolicy(options, logger, name);
    }

    public Dictionary<string, RetryPolicyStatistics> GetAllStatistics()
    {
        return _policies.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStatistics());
    }

    public void ResetAllStatistics()
    {
        foreach (var policy in _policies.Values)
        {
            policy.ResetStatistics();
        }
    }

    private IRetryPolicy CreateRetryPolicyInternal(string name)
    {
        var options = _optionsMonitor.Get(name);
        var logger = _loggerFactory.CreateLogger<RetryPolicy>();
        return new RetryPolicy(options, logger, name);
    }
}

/// <summary>
/// Interface for retry policy factory
/// </summary>
public interface IRetryPolicyFactory
{
    /// <summary>
    /// Get or create a retry policy by name
    /// </summary>
    IRetryPolicy GetRetryPolicy(string name);

    /// <summary>
    /// Create a retry policy with specific options
    /// </summary>
    IRetryPolicy CreateRetryPolicy(string name, RetryPolicyOptions options);

    /// <summary>
    /// Get statistics for all retry policies
    /// </summary>
    Dictionary<string, RetryPolicyStatistics> GetAllStatistics();

    /// <summary>
    /// Reset statistics for all retry policies
    /// </summary>
    void ResetAllStatistics();
}

/// <summary>
/// Sends through a circuit breaker and a retry policy, retrying only what a
/// second attempt could answer differently.
/// </summary>
/// <remarks>
/// A status code is an answer, and the caller is entitled to it. Only a
/// transient one is worth asking again: a timeout, a throttle, a server that
/// broke. A <c>404</c>, a <c>400</c>, a <c>403</c> will say the same thing three
/// times, and asking anyway costs the far service three times the work and hands
/// the near one a transport failure where an answer belongs.
/// </remarks>
public class ResilientHttpPolicyHandler : DelegatingHandler
{
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly IRetryPolicy _retryPolicy;

    public ResilientHttpPolicyHandler(ICircuitBreaker circuitBreaker, IRetryPolicy retryPolicy)
    {
        _circuitBreaker = circuitBreaker;
        _retryPolicy = retryPolicy;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _circuitBreaker.ExecuteAsync(
                () => _retryPolicy.ExecuteAsync(
                    async () =>
                    {
                        // A fresh message each attempt: content is a stream, and
                        // a sent message cannot be sent again.
                        using var attempt = await CloneAsync(request, cancellationToken);
                        var response = await base.SendAsync(attempt, cancellationToken);

                        // The retry policy retries on exceptions, so this is how
                        // a transient status enters it — carrying the response
                        // rather than describing it, so the last attempt can
                        // still be answered with what came back.
                        return IsWorthRetrying(response.StatusCode)
                            ? throw new TransientHttpResponseException(response)
                            : response;
                    },
                    cancellationToken),
                cancellationToken);
        }
        catch (TransientHttpResponseException exhausted)
        {
            // Out of attempts. The far service did answer, and its answer is
            // more use to the caller than a failure to have asked.
            return exhausted.Response;
        }
    }

    /// <summary>
    /// Whether asking again could plausibly get a different answer.
    /// </summary>
    /// <remarks>
    /// <c>501</c> is left out of the server errors on purpose: a route that is
    /// not implemented will not be implemented between two attempts.
    /// </remarks>
    private static bool IsWorthRetrying(HttpStatusCode status) => status switch
    {
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.NotImplemented => false,
        _ => (int)status >= 500
    };

    /// <summary>Copies a request so it can be sent again.</summary>
    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
            VersionPolicy = request.VersionPolicy
        };

        if (request.Content is not null)
        {
            var buffered = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(buffered);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in (IDictionary<string, object?>)request.Options)
        {
            clone.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        }

        return clone;
    }
}

/// <summary>
/// Carries a response whose status is worth another attempt.
/// </summary>
/// <remarks>
/// Internal to the handler: it exists because the retry policy signals through
/// exceptions, and it holds the response so that running out of attempts still
/// yields the far service's answer.
/// </remarks>
internal sealed class TransientHttpResponseException(HttpResponseMessage response)
    : Exception($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}")
{
    public HttpResponseMessage Response { get; } = response;
}