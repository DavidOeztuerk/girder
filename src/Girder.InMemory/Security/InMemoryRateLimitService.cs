using Girder.Abstractions.Security.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Girder.InMemory.Security;

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
