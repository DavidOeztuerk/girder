using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Infrastructure.Security.RateLimiting;

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
    // Check environment variable as fallback
    var redisConnectionString = configuration.GetConnectionString("Redis")
        ?? configuration["Redis:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");

    if (!string.IsNullOrEmpty(redisConnectionString))
    {
      // Redis-based rate limiting
      services.AddSingleton<IRateLimitService, RateLimitService>();
    }
    else
    {
      // In-memory fallback
      services.AddSingleton<IRateLimitService, InMemoryRateLimitService>();
    }

    // Add middleware
    services.AddTransient<RateLimitMiddleware>();

    // Add background services
    services.AddHostedService<RateLimitMaintenanceService>();

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

/// <summary>
/// Background service for rate limit maintenance
/// </summary>
public class RateLimitMaintenanceService : BackgroundService
{
  private readonly IRateLimitService _rateLimitService;
  private readonly Microsoft.Extensions.Logging.ILogger<RateLimitMaintenanceService> _logger;
  private readonly TimeSpan _maintenanceInterval = TimeSpan.FromHours(1);
  private const int BlacklistThreshold = 1000;
  private const int RuleAdjustmentThreshold = 500;
  private const double RuleAdjustmentFactor = 0.8;

  public RateLimitMaintenanceService(
      IRateLimitService rateLimitService,
      Microsoft.Extensions.Logging.ILogger<RateLimitMaintenanceService> logger)
  {
    _rateLimitService = rateLimitService;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Rate limit maintenance service started");

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PerformMaintenanceTasks(stoppingToken);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during rate limit maintenance");
      }

      try
      {
        await Task.Delay(_maintenanceInterval, stoppingToken);
      }
      catch (OperationCanceledException)
      {
        break;
      }
    }

    _logger.LogInformation("Rate limit maintenance service stopped");
  }

  private async Task PerformMaintenanceTasks(CancellationToken cancellationToken)
  {
    _logger.LogDebug("Starting rate limit maintenance tasks");

    try
    {
      // Generate and log statistics
      var statistics = await _rateLimitService.GetStatisticsAsync(
          DateTime.UtcNow.AddHours(-24),
          DateTime.UtcNow,
          cancellationToken);

      if (statistics.TotalViolations > 0)
      {
        _logger.LogInformation(
            "Rate limit statistics (last 24h): {TotalViolations} violations, {UniqueClients} unique clients limited",
            statistics.TotalViolations, statistics.UniqueClientsLimited);

        // Log top violating clients
        if (statistics.TopViolatingClients.Any())
        {
          var topViolators = string.Join(", ",
              statistics.TopViolatingClients.Take(5).Select(kvp => $"{kvp.Key}({kvp.Value})"));
          _logger.LogInformation("Top violating clients: {TopViolators}", topViolators);
        }
      }

      foreach (var violator in statistics.TopViolatingClients.Where(v => v.Value > BlacklistThreshold))
      {
        await _rateLimitService.BlacklistClientAsync(
            violator.Key,
            TimeSpan.FromHours(24),
            "Automatic blacklist for excessive violations",
            cancellationToken);
        _logger.LogWarning(
            "Automatically blacklisted {ClientId} for exceeding {Count} violations",
            violator.Key,
            violator.Value);
      }

      var rules = _rateLimitService.GetRegisteredRules();
      foreach (var rule in rules)
      {
        if (statistics.ViolationsByRule.TryGetValue(rule.Id, out var count) && count > RuleAdjustmentThreshold)
        {
          var newLimit = (long)Math.Max(1, rule.Configuration.RequestLimit * RuleAdjustmentFactor);
          if (newLimit < rule.Configuration.RequestLimit)
          {
            rule.Configuration.RequestLimit = newLimit;
            rule.ModifiedAt = DateTime.UtcNow;
            await _rateLimitService.RegisterRuleAsync(rule, cancellationToken);
            _logger.LogInformation(
                "Adjusted rule {RuleId} due to {Count} violations. New limit: {Limit}",
                rule.Id,
                count,
                newLimit);
          }
        }
      }

      if (statistics.TotalViolations > BlacklistThreshold)
      {
        _logger.LogWarning(
            "High number of rate limit violations detected: {Count}. Review configuration for potential optimizations.",
            statistics.TotalViolations);
      }

    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Error during rate limit statistics collection");
    }

    _logger.LogDebug("Rate limit maintenance tasks completed");
  }
}

/// <summary>
/// In-memory rate limit service for development/testing
/// </summary>
public class InMemoryRateLimitService : IRateLimitService
{
  private readonly Dictionary<string, RateLimitRule> _rules = new();
  private readonly Dictionary<string, Dictionary<string, RateLimitWindow>> _windows = new();
  private readonly Dictionary<string, List<RateLimitViolation>> _violations = new();
  private readonly HashSet<string> _whitelist = new();
  private readonly HashSet<string> _blacklist = new();
  private readonly object _lock = new();
  private readonly Microsoft.Extensions.Logging.ILogger<InMemoryRateLimitService> _logger;

  public InMemoryRateLimitService(Microsoft.Extensions.Logging.ILogger<InMemoryRateLimitService> logger)
  {
    _logger = logger;
  }

  public Task<RateLimitResult> CheckRateLimitAsync(RateLimitRequest request, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      // Check whitelist
      if (_whitelist.Contains(request.ClientId))
      {
        return Task.FromResult(new RateLimitResult { IsAllowed = true });
      }

      // Check blacklist
      if (_blacklist.Contains(request.ClientId))
      {
        return Task.FromResult(new RateLimitResult
        {
          IsAllowed = false,
          Reason = "Client is blacklisted",
          Severity = RateLimitSeverity.Critical
        });
      }

      // Check rules
      foreach (var rule in _rules.Values.Where(r => r.IsEnabled).OrderByDescending(r => r.Priority))
      {
        if (IsRuleApplicable(rule, request))
        {
          var result = CheckRuleInMemory(request, rule);
          if (!result.IsAllowed)
          {
            LogViolationInMemory(request, rule, result);
            return Task.FromResult(result);
          }
        }
      }

      return Task.FromResult(new RateLimitResult { IsAllowed = true });
    }
  }

  public Task RegisterRuleAsync(RateLimitRule rule, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      _rules[rule.Id] = rule;
      _logger.LogInformation("Registered rate limit rule (in-memory): {RuleId} - {RuleName}", rule.Id, rule.Name);
    }
    return Task.CompletedTask;
  }

  public Task RemoveRuleAsync(string ruleId, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      if (_rules.Remove(ruleId))
      {
        _logger.LogInformation("Removed rate limit rule (in-memory): {RuleId}", ruleId);
      }
    }
    return Task.CompletedTask;
  }

  public Task<RateLimitStatus> GetStatusAsync(string clientId, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      var status = new RateLimitStatus
      {
        ClientId = clientId,
        IsWhitelisted = _whitelist.Contains(clientId),
        IsBlacklisted = _blacklist.Contains(clientId)
      };

      if (_windows.TryGetValue(clientId, out var clientWindows))
      {
        status.ActiveWindows = clientWindows.Values.ToList();
        status.IsRateLimited = clientWindows.Values.Any(w => w.IsExceeded);
      }

      if (_violations.TryGetValue(clientId, out var clientViolations))
      {
        status.RecentViolations = clientViolations.Where(v => v.Timestamp > DateTime.UtcNow.AddHours(-24)).ToList();
      }

      return Task.FromResult(status);
    }
  }

  public Task ResetLimitsAsync(string clientId, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      _windows.Remove(clientId);
      _violations.Remove(clientId);
      _logger.LogInformation("Reset rate limits for client (in-memory): {ClientId}", clientId);
    }
    return Task.CompletedTask;
  }

  public Task<RateLimitStatistics> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      var statistics = new RateLimitStatistics();
      var from = fromDate ?? DateTime.MinValue;
      var to = toDate ?? DateTime.MaxValue;

      foreach (var clientViolations in _violations.Values)
      {
        var relevantViolations = clientViolations.Where(v => v.Timestamp >= from && v.Timestamp <= to);
        statistics.TotalViolations += relevantViolations.Count();
      }

      return Task.FromResult(statistics);
    }
  }

  public Task SetClientLimitsAsync(string clientId, Dictionary<string, RateLimitConfiguration> limits, CancellationToken cancellationToken = default)
  {
    // Simplified implementation for in-memory version
    _logger.LogInformation("Set custom limits for client (in-memory): {ClientId}", clientId);
    return Task.CompletedTask;
  }

  public Task WhitelistClientAsync(string clientId, TimeSpan? duration = null, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      _whitelist.Add(clientId);
      _logger.LogInformation("Whitelisted client (in-memory): {ClientId}", clientId);
    }
    return Task.CompletedTask;
  }

  public Task BlacklistClientAsync(string clientId, TimeSpan? duration = null, string? reason = null, CancellationToken cancellationToken = default)
  {
    lock (_lock)
    {
      _blacklist.Add(clientId);
      _logger.LogWarning("Blacklisted client (in-memory): {ClientId}, Reason: {Reason}", clientId, reason);
    }
    return Task.CompletedTask;
  }

  public IEnumerable<RateLimitRule> GetRegisteredRules()
  {
    lock (_lock)
    {
      return _rules.Values.Select(r => r).ToList();
    }
  }

  private RateLimitResult CheckRuleInMemory(RateLimitRequest request, RateLimitRule rule)
  {
    if (!_windows.ContainsKey(request.ClientId))
    {
      _windows[request.ClientId] = new Dictionary<string, RateLimitWindow>();
    }

    var clientWindows = _windows[request.ClientId];
    var now = DateTime.UtcNow;
    var windowStart = now.Subtract(rule.Configuration.Window);

    if (!clientWindows.TryGetValue(rule.Id, out var window) || window.StartTime < windowStart)
    {
      window = new RateLimitWindow
      {
        RuleId = rule.Id,
        StartTime = windowStart,
        EndTime = now,
        RequestCount = 0,
        Limit = rule.Configuration.RequestLimit
      };
      clientWindows[rule.Id] = window;
    }

    window.RequestCount++;
    window.EndTime = now;
    window.IsExceeded = window.RequestCount > window.Limit;

    if (window.IsExceeded)
    {
      return new RateLimitResult
      {
        IsAllowed = false,
        TriggeredRule = rule,
        CurrentCount = window.RequestCount,
        Limit = window.Limit,
        Remaining = 0,
        ResetTime = windowStart.Add(rule.Configuration.Window),
        Reason = $"Rate limit exceeded for rule: {rule.Name}"
      };
    }

    return new RateLimitResult
    {
      IsAllowed = true,
      CurrentCount = window.RequestCount,
      Limit = window.Limit,
      Remaining = window.Limit - window.RequestCount,
      ResetTime = windowStart.Add(rule.Configuration.Window)
    };
  }

  private void LogViolationInMemory(RateLimitRequest request, RateLimitRule rule, RateLimitResult result)
  {
    if (!_violations.ContainsKey(request.ClientId))
    {
      _violations[request.ClientId] = new List<RateLimitViolation>();
    }

    _violations[request.ClientId].Add(new RateLimitViolation
    {
      Timestamp = DateTime.UtcNow,
      RuleId = rule.Id,
      Endpoint = request.Endpoint,
      RequestCount = result.CurrentCount,
      Limit = result.Limit,
      Severity = result.Severity
    });

    // Keep only recent violations
    var cutoff = DateTime.UtcNow.AddDays(-7);
    _violations[request.ClientId].RemoveAll(v => v.Timestamp < cutoff);
  }

  private static bool IsRuleApplicable(RateLimitRule rule, RateLimitRequest request)
  {
    // Simplified rule matching for in-memory implementation
    var conditions = rule.Conditions;

    if (conditions.UserRoles.Any() && !conditions.UserRoles.Any(role => request.UserRoles.Contains(role)))
      return false;

    if (conditions.Endpoints.Any() && !conditions.Endpoints.Any(endpoint => request.Endpoint.Contains(endpoint)))
      return false;

    if (conditions.Methods.Any() && !conditions.Methods.Contains(request.Method))
      return false;

    return true;
  }
}