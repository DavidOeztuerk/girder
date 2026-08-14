using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Infrastructure.Resilience;

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
/// HTTP policy handler that combines circuit breaker and retry policy
/// </summary>
public class ResilientHttpPolicyHandler : DelegatingHandler
{
    private readonly ICircuitBreaker _circuitBreaker;
    private readonly IRetryPolicy _retryPolicy;

    public ResilientHttpPolicyHandler(ICircuitBreaker circuitBreaker, IRetryPolicy retryPolicy)
    {
        _circuitBreaker = circuitBreaker;
        _retryPolicy = retryPolicy;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _circuitBreaker.ExecuteAsync(async () =>
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                var response = await base.SendAsync(request, cancellationToken);
                
                // Consider non-success status codes as failures for retry logic
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"HTTP request failed with status {response.StatusCode}: {response.ReasonPhrase}");
                }
                
                return response;
            }, cancellationToken);
        }, cancellationToken);
    }
}