using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Infrastructure.Resilience;

/// <summary>
/// Factory for creating and managing circuit breakers
/// </summary>
public class CircuitBreakerFactory : ICircuitBreakerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IOptionsMonitor<CircuitBreakerOptions> _optionsMonitor;
    private readonly ConcurrentDictionary<string, ICircuitBreaker> _circuitBreakers = new();

    public CircuitBreakerFactory(
        ILoggerFactory loggerFactory,
        IOptionsMonitor<CircuitBreakerOptions> optionsMonitor)
    {
        _loggerFactory = loggerFactory;
        _optionsMonitor = optionsMonitor;
    }

    public ICircuitBreaker GetCircuitBreaker(string name)
    {
        return _circuitBreakers.GetOrAdd(name, CreateCircuitBreaker);
    }

    public ICircuitBreaker GetCircuitBreaker(string name, CircuitBreakerOptions options)
    {
        var key = $"{name}_{options.GetHashCode()}";
        return _circuitBreakers.GetOrAdd(key, _ => CreateCircuitBreaker(name, options));
    }

    public void RemoveCircuitBreaker(string name)
    {
        _circuitBreakers.TryRemove(name, out _);
    }

    public IEnumerable<string> GetCircuitBreakerNames()
    {
        return _circuitBreakers.Keys;
    }

    public Dictionary<string, CircuitBreakerStatistics> GetAllStatistics()
    {
        return _circuitBreakers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStatistics());
    }

    public void ResetAll()
    {
        foreach (var circuitBreaker in _circuitBreakers.Values)
        {
            circuitBreaker.Reset();
        }
    }

    private ICircuitBreaker CreateCircuitBreaker(string name)
    {
        var options = _optionsMonitor.Get(name);
        return CreateCircuitBreaker(name, options);
    }

    private ICircuitBreaker CreateCircuitBreaker(string name, CircuitBreakerOptions options)
    {
        var logger = _loggerFactory.CreateLogger<CircuitBreaker>();
        return new CircuitBreaker(options, logger, name);
    }
}

/// <summary>
/// Interface for circuit breaker factory
/// </summary>
public interface ICircuitBreakerFactory
{
    /// <summary>
    /// Get or create a circuit breaker by name
    /// </summary>
    ICircuitBreaker GetCircuitBreaker(string name);

    /// <summary>
    /// Get or create a circuit breaker with specific options
    /// </summary>
    ICircuitBreaker GetCircuitBreaker(string name, CircuitBreakerOptions options);

    /// <summary>
    /// Remove a circuit breaker
    /// </summary>
    void RemoveCircuitBreaker(string name);

    /// <summary>
    /// Get all circuit breaker names
    /// </summary>
    IEnumerable<string> GetCircuitBreakerNames();

    /// <summary>
    /// Get statistics for all circuit breakers
    /// </summary>
    Dictionary<string, CircuitBreakerStatistics> GetAllStatistics();

    /// <summary>
    /// Reset all circuit breakers
    /// </summary>
    void ResetAll();
}