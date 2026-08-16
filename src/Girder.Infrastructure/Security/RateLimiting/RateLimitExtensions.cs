using Girder.Abstractions.Security.RateLimiting;
using Girder.Abstractions.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Security.RateLimiting;

/// <summary>
/// Extension methods for rate limiting services
/// </summary>
public static class RateLimitExtensions
{
  /// <summary>
  /// Add rate limiting services
  /// </summary>
  public static IServiceCollection AddRateLimit(
      this IServiceCollection services,
      IConfiguration configuration)
  {
    // The IRateLimitService implementation comes from a provider package —
    // AddRedisRateLimiting() or AddInMemoryRateLimiting(). Whether counters are
    // shared between instances decides how much traffic actually gets through,
    // so it is not something a missing connection string should settle.

    // Add middleware
    services.AddTransient<RateLimitMiddleware>();

    // Add background services

    // Configure options
    services.Configure<RateLimitOptions>(configuration.GetSection("RateLimit"));

    return services;
  }

  /// <summary>
  /// Add rate limiting middleware
  /// </summary>
  public static IServiceCollection AddRateLimitMiddleware(this IServiceCollection services)
  {
    services.AddTransient<RateLimitMiddleware>();
    return services;
  }

  /// <summary>
  /// Configure rate limiting rules
  /// </summary>
  public static IServiceCollection ConfigureRateLimitRules(
      this IServiceCollection services,
      Action<IRateLimitRuleBuilder> configure)
  {
    var builder = new RateLimitRuleBuilder();
    configure(builder);

    services.AddSingleton(provider =>
    {
      var rateLimitService = provider.GetRequiredService<IRateLimitService>();

      // Register all configured rules
      foreach (var rule in builder.Rules)
      {
        rateLimitService.RegisterRuleAsync(rule).Wait();
      }

      return rateLimitService;
    });

    return services;
  }
}

/// <summary>
/// Builder for configuring rate limit rules
/// </summary>
public interface IRateLimitRuleBuilder
{
  /// <summary>
  /// Add a global rate limit rule
  /// </summary>
  IRateLimitRuleBuilder AddGlobalRule(string name, long requestLimit, TimeSpan window, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow);

  /// <summary>
  /// Add a role-based rate limit rule
  /// </summary>
  IRateLimitRuleBuilder AddRoleRule(string name, string[] roles, long requestLimit, TimeSpan window, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow);

  /// <summary>
  /// Add an endpoint-specific rate limit rule
  /// </summary>
  IRateLimitRuleBuilder AddEndpointRule(string name, string[] endpoints, long requestLimit, TimeSpan window, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow);

  /// <summary>
  /// Add a custom rate limit rule
  /// </summary>
  IRateLimitRuleBuilder AddCustomRule(RateLimitRule rule);

  /// <summary>
  /// Add a time-based rate limit rule
  /// </summary>
  IRateLimitRuleBuilder AddTimeBasedRule(string name, TimeConditions timeConditions, long requestLimit, TimeSpan window);

  /// <summary>
  /// Add a burst protection rule
  /// </summary>
  IRateLimitRuleBuilder AddBurstProtectionRule(string name, long burstLimit, double refillRate, TimeSpan window);

  /// <summary>
  /// Get all configured rules
  /// </summary>
  List<RateLimitRule> Rules { get; }
}

/// <summary>
/// Implementation of rate limit rule builder
/// </summary>
public class RateLimitRuleBuilder : IRateLimitRuleBuilder
{
  public List<RateLimitRule> Rules { get; } = new();

  public IRateLimitRuleBuilder AddGlobalRule(string name, long requestLimit, TimeSpan window, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow)
  {
    var rule = new RateLimitRule
    {
      Id = $"global-{name}".ToLowerInvariant().Replace(" ", "-"),
      Name = name,
      Description = $"Global rate limit: {requestLimit} requests per {window}",
      Configuration = new RateLimitConfiguration
      {
        RequestLimit = requestLimit,
        Window = window,
        Algorithm = algorithm
      },
      Conditions = new RateLimitConditions(),
      Priority = 50
    };

    Rules.Add(rule);
    return this;
  }

  public IRateLimitRuleBuilder AddRoleRule(string name, string[] roles, long requestLimit, TimeSpan window, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow)
  {
    var rule = new RateLimitRule
    {
      Id = $"role-{name}".ToLowerInvariant().Replace(" ", "-"),
      Name = name,
      Description = $"Role-based rate limit for {string.Join(", ", roles)}: {requestLimit} requests per {window}",
      Configuration = new RateLimitConfiguration
      {
        RequestLimit = requestLimit,
        Window = window,
        Algorithm = algorithm
      },
      Conditions = new RateLimitConditions
      {
        UserRoles = roles.ToList()
      },
      Priority = 100
    };

    Rules.Add(rule);
    return this;
  }

  public IRateLimitRuleBuilder AddEndpointRule(string name, string[] endpoints, long requestLimit, TimeSpan window, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow)
  {
    var rule = new RateLimitRule
    {
      Id = $"endpoint-{name}".ToLowerInvariant().Replace(" ", "-"),
      Name = name,
      Description = $"Endpoint-specific rate limit for {string.Join(", ", endpoints)}: {requestLimit} requests per {window}",
      Configuration = new RateLimitConfiguration
      {
        RequestLimit = requestLimit,
        Window = window,
        Algorithm = algorithm
      },
      Conditions = new RateLimitConditions
      {
        Endpoints = endpoints.ToList()
      },
      Priority = 150
    };

    Rules.Add(rule);
    return this;
  }

  public IRateLimitRuleBuilder AddCustomRule(RateLimitRule rule)
  {
    Rules.Add(rule);
    return this;
  }

  public IRateLimitRuleBuilder AddTimeBasedRule(string name, TimeConditions timeConditions, long requestLimit, TimeSpan window)
  {
    var rule = new RateLimitRule
    {
      Id = $"time-{name}".ToLowerInvariant().Replace(" ", "-"),
      Name = name,
      Description = $"Time-based rate limit: {requestLimit} requests per {window}",
      Configuration = new RateLimitConfiguration
      {
        RequestLimit = requestLimit,
        Window = window,
        Algorithm = RateLimitAlgorithm.SlidingWindow
      },
      Conditions = new RateLimitConditions
      {
        TimeConditions = timeConditions
      },
      Priority = 75
    };

    Rules.Add(rule);
    return this;
  }

  public IRateLimitRuleBuilder AddBurstProtectionRule(string name, long burstLimit, double refillRate, TimeSpan window)
  {
    var rule = new RateLimitRule
    {
      Id = $"burst-{name}".ToLowerInvariant().Replace(" ", "-"),
      Name = name,
      Description = $"Burst protection: {burstLimit} burst limit, {refillRate} refill rate",
      Configuration = new RateLimitConfiguration
      {
        RequestLimit = burstLimit,
        Window = window,
        Algorithm = RateLimitAlgorithm.TokenBucket,
        BurstLimit = burstLimit,
        RefillRate = refillRate
      },
      Conditions = new RateLimitConditions(),
      Priority = 200
    };

    Rules.Add(rule);
    return this;
  }
}
