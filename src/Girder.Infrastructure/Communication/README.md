# Service Communication Infrastructure

## 🚀 Overview

The Service Communication Infrastructure provides a production-ready, enterprise-grade solution for inter-service communication in the Skillswap microservices architecture.

## ✨ Features Implemented

### Phase 1: Retry Policy Integration ✅
- **Exponential Backoff with Jitter**: Prevents thundering herd problem
- **Configurable Retry Strategies**: Linear, Exponential, Fibonacci
- **Smart Retry Logic**: Retries only transient failures (408, 429, 500, 502, 503, 504)
- **Automatic Integration**: Works seamlessly with Circuit Breaker

### Phase 2: Response Caching Layer ✅
- **Distributed Caching**: Uses Redis or Memory cache
- **Time-Based TTL**: Configurable per service
- **Cache Key Generation**: Smart key generation based on endpoint + params
- **Per-Service Policies**: Different cache policies for different services
- **Pattern-Based Exclusion**: Regex patterns to exclude endpoints from caching

### Phase 3: M2M Authentication ✅
- **Client Credentials Flow**: OAuth 2.0 compliant
- **Automatic Token Refresh**: Tokens refreshed 5 minutes before expiry
- **Token Caching**: Tokens cached in distributed cache
- **Graceful Fallback**: Falls back to configured token if M2M fails
- **Scope-Based Authorization**: Support for multiple scopes

### Phase 4: Metrics & Telemetry ✅
- **Comprehensive Metrics**: Request count, latency, errors per service/endpoint
- **Percentile Tracking**: P50, P95, P99 response times
- **Status Code Distribution**: Track all HTTP status codes
- **Cache Metrics**: Hit rate, miss rate per service
- **Retry Metrics**: Track retry attempts per service
- **Circuit Breaker Metrics**: Monitor circuit breaker states

### Phase 7: Request Deduplication ✅
- **Single Flight Pattern**: Coalesce duplicate concurrent requests
- **Automatic Key Generation**: Based on service + endpoint + parameters
- **Statistics Tracking**: Monitor deduplication rate
- **Thread-Safe**: Handles race conditions gracefully

### Phase 8: Bulkhead Isolation ✅
- **Semaphore-Based Limiting**: Controls concurrent requests per service
- **Queue Management**: Queues excess requests instead of rejecting
- **Per-Service Limits**: Different limits for different services
- **Graceful Degradation**: Fallback when bulkhead is full

## 📋 Configuration

### appsettings.json Template

```json
{
  "ServiceCommunication": {
    "UseGateway": true,
    "DefaultTimeout": "00:00:30",
    "EnableResponseCaching": true,
    "EnableMetrics": true,
    "EnableRequestDeduplication": false,

    "RetryPolicy": {
      "MaxRetries": 3,
      "BackoffStrategy": "ExponentialBackoff",
      "InitialDelay": "00:00:01",
      "MaxDelay": "00:00:10",
      "RetryOnTimeout": true,
      "UseJitter": true,
      "JitterFactor": 0.2,
      "RetryableStatusCodes": [408, 429, 500, 502, 503, 504],
      "RetryableExceptions": [
        "TimeoutException",
        "HttpRequestException",
        "TaskCanceledException"
      ]
    },

    "CircuitBreaker": {
      "ExceptionsAllowedBeforeBreaking": 5,
      "DurationOfBreak": "00:00:30",
      "Timeout": "00:00:10",
      "FailureThreshold": 0.5,
      "MinimumThroughput": 10
    },

    "Caching": {
      "DefaultTTL": "00:05:00",
      "MaxCacheSize": 1000,
      "EnableETagSupport": true,
      "RespectCacheControlHeaders": true,
      "CacheKeyPrefix": "svc:",
      "EnableCompression": true,
      "CompressionThreshold": 1024,
      "PerServicePolicies": {
        "userservice": {
          "Enabled": true,
          "TTL": "00:10:00",
          "CacheOnlySuccess": true,
          "CacheableMethods": ["GET", "HEAD"],
          "ExcludePatterns": [".*\\/health.*", ".*\\/metrics.*"],
          "IncludePatterns": [".*\\/api\\/users\\/profile\\/.*"]
        },
        "skillservice": {
          "Enabled": true,
          "TTL": "00:15:00",
          "CacheOnlySuccess": true,
          "CacheableMethods": ["GET"],
          "ExcludePatterns": [],
          "IncludePatterns": []
        },
        "matchmakingservice": {
          "Enabled": false,
          "TTL": "00:02:00",
          "CacheOnlySuccess": true,
          "CacheableMethods": ["GET"],
          "ExcludePatterns": [".*\\/matches\\/active.*"],
          "IncludePatterns": []
        }
      }
    },

    "M2M": {
      "Enabled": false,
      "TokenEndpoint": "https://auth.skillswap.com/oauth/token",
      "ClientId": "skillswap-service-client",
      "ClientSecret": "YOUR_CLIENT_SECRET_HERE",
      "Scopes": ["api.read", "api.write"],
      "TokenLifetime": "01:00:00",
      "RefreshBeforeExpiry": "00:05:00",
      "EnableTokenCaching": true,
      "TokenCacheKeyPrefix": "m2m:token:",
      "UseMutualTLS": false,
      "ClientCertificatePath": null,
      "ClientCertificatePassword": null
    },

    "Bulkhead": {
      "MaxParallelRequests": 100,
      "MaxQueuedRequests": 50,
      "PerServiceLimits": {
        "userservice": 50,
        "skillservice": 50,
        "matchmakingservice": 30,
        "appointmentservice": 30
      }
    },

    "Telemetry": {
      "EnableDetailedMetrics": true,
      "TraceBodyContent": false,
      "SampleRate": 1.0
    },

    "ServiceEndpoints": {
      "UserService": "http://localhost:5001",
      "SkillService": "http://localhost:5002",
      "MatchmakingService": "http://localhost:5003",
      "AppointmentService": "http://localhost:5004",
      "VideocallService": "http://localhost:5005",
      "NotificationService": "http://localhost:5006",
      "Gateway": "http://localhost:8080"
    },

    "BearerToken": null
  }
}
```

### Environment-Specific Configuration

**Development (appsettings.Development.json):**
```json
{
  "ServiceCommunication": {
    "UseGateway": false,
    "EnableResponseCaching": false,
    "M2M": {
      "Enabled": false
    },
    "Telemetry": {
      "TraceBodyContent": true
    }
  }
}
```

**Production (appsettings.Production.json):**
```json
{
  "ServiceCommunication": {
    "UseGateway": true,
    "EnableResponseCaching": true,
    "M2M": {
      "Enabled": true,
      "TokenEndpoint": "https://auth.skillswap.com/oauth/token"
    },
    "Telemetry": {
      "TraceBodyContent": false,
      "SampleRate": 0.1
    }
  }
}
```

## 🔧 Usage Examples

### Basic GET Request
```csharp
public class UserServiceClient
{
    private readonly IServiceCommunicationManager _serviceCommunication;

    public async Task<UserProfile?> GetUserProfileAsync(string userId)
    {
        return await _serviceCommunication.GetAsync<UserProfile>(
            serviceName: "userservice",
            endpoint: $"api/users/profile/{userId}"
        );
    }
}
```

### POST Request with Caching Bypass
```csharp
public async Task<CreateUserResponse?> CreateUserAsync(CreateUserRequest request)
{
    // POST requests are not cached by default
    return await _serviceCommunication.SendRequestAsync<CreateUserRequest, CreateUserResponse>(
        serviceName: "userservice",
        endpoint: "api/users",
        request: request
    );
}
```

### With Custom Headers
```csharp
public async Task<SkillDetails?> GetSkillDetailsAsync(string skillId)
{
    var headers = new Dictionary<string, string>
    {
        ["X-Custom-Header"] = "CustomValue"
    };

    return await _serviceCommunication.GetAsync<SkillDetails>(
        serviceName: "skillservice",
        endpoint: $"api/skills/{skillId}",
        headers: headers
    );
}
```

## 📊 Monitoring & Metrics

### Comprehensive Service Communication Metrics
```csharp
public class MetricsController : ControllerBase
{
    private readonly IServiceCommunicationMetrics _metrics;

    [HttpGet("api/metrics/summary")]
    public ActionResult<ServiceCommunicationMetricsSummary> GetMetricsSummary()
    {
        var summary = _metrics.GetMetricsSummary();
        // summary.TotalRequests
        // summary.SuccessfulRequests
        // summary.CachedRequests
        // summary.RetryAttempts
        // summary.SuccessRate
        // summary.CacheHitRate
        // summary.AverageResponseTime
        // summary.ServiceMetrics (per-service breakdown)
        return Ok(summary);
    }

    [HttpGet("api/metrics/service/{serviceName}")]
    public ActionResult<ServiceMetrics> GetServiceMetrics(string serviceName)
    {
        var metrics = _metrics.GetServiceMetrics(serviceName);
        // metrics.TotalRequests
        // metrics.AverageResponseTime
        // metrics.P50ResponseTime
        // metrics.P95ResponseTime
        // metrics.P99ResponseTime
        // metrics.SuccessRate
        // metrics.EndpointMetrics (per-endpoint breakdown)
        // metrics.CircuitBreakerState
        return Ok(metrics);
    }
}
```

### Cache Statistics
```csharp
public class CacheMonitoringService
{
    private readonly IServiceResponseCache _cache;

    public CacheStatistics GetCacheStats()
    {
        var stats = _cache.GetStatistics();
        // stats.TotalRequests
        // stats.CacheHits
        // stats.CacheMisses
        // stats.HitRate
        return stats;
    }
}
```

### Request Deduplication Statistics
```csharp
public class DeduplicationMonitoringService
{
    private readonly IRequestDeduplicator _deduplicator;

    public DeduplicationStatistics GetDeduplicationStats()
    {
        var stats = _deduplicator.GetStatistics();
        // stats.TotalRequests
        // stats.DeduplicatedRequests
        // stats.UniqueRequests
        // stats.DeduplicationRate
        // stats.CurrentInflightRequests
        return stats;
    }
}
```

### Circuit Breaker Statistics
```csharp
public class ResilienceMonitoringService
{
    private readonly ICircuitBreakerFactory _circuitBreakerFactory;

    public Dictionary<string, CircuitBreakerStatistics> GetAllCircuitBreakerStats()
    {
        return _circuitBreakerFactory.GetAllCircuitBreakerStats();
    }
}
```

## 🎯 Best Practices

### 1. Cache Configuration
- **Cache User Profiles**: 10-15 minutes TTL (data rarely changes)
- **Cache Skill Catalog**: 15-30 minutes TTL (relatively static)
- **Don't Cache**: Match requests, active appointments, real-time data
- **Use Pattern Exclusion**: Exclude health checks, metrics endpoints

### 2. Retry Configuration
- **Max Retries**: 3 (balance between resilience and latency)
- **Initial Delay**: 1 second (don't overwhelm failing service)
- **Max Delay**: 10 seconds (prevent infinite waiting)
- **Use Jitter**: Always enable to prevent thundering herd

### 3. M2M Authentication
- **Production Only**: Enable M2M in production, not development
- **Secure Secrets**: Store ClientSecret in Azure Key Vault/AWS Secrets Manager
- **Scope Principle**: Request minimum required scopes
- **Token Lifetime**: 1 hour is optimal (balance between performance and security)

### 4. Circuit Breaker
- **Exceptions Before Breaking**: 5 (catches repeated failures)
- **Duration of Break**: 30 seconds (gives service time to recover)
- **Failure Threshold**: 50% (trip when half of requests fail)

## 🚨 Troubleshooting

### Issue: Cache not working
**Solution:**
1. Check `EnableResponseCaching` is `true`
2. Verify Redis is running and accessible
3. Check service-specific cache policy is not `Enabled: false`
4. Ensure endpoint doesn't match `ExcludePatterns`

### Issue: M2M token errors
**Solution:**
1. Verify `TokenEndpoint` is correct
2. Check `ClientId` and `ClientSecret` are valid
3. Ensure scopes are allowed for the client
4. Check logs for detailed error messages

### Issue: Too many retries
**Solution:**
1. Reduce `MaxRetries` to 2 or 1
2. Check if downstream service is actually down
3. Verify `RetryableStatusCodes` doesn't include permanent errors (400, 401, 403, 404)

## 🔐 Security Considerations

1. **Token Management**: Never commit ClientSecret to Git
2. **Cache Keys**: Don't include sensitive data in cache keys
3. **Logging**: Don't log tokens or sensitive request/response bodies
4. **TLS**: Always use HTTPS in production
5. **Scopes**: Follow principle of least privilege

## 🛠️ Migration Guide

### From Old ServiceCommunicationManager to New

**Step 1:** Update appsettings.json with new structure (see template above)

**Step 2:** No code changes required! The new implementation is backward compatible.

**Step 3:** Gradually enable features:
```json
{
  "ServiceCommunication": {
    "EnableResponseCaching": true,  // Start with caching
    "M2M": {
      "Enabled": false  // Enable M2M later after testing
    }
  }
}
```

**Step 4:** Monitor metrics and adjust configuration as needed.

## 📚 Additional Resources

- [Circuit Breaker Pattern](https://martinfowler.com/bliki/CircuitBreaker.html)
- [Retry Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/retry)
- [OAuth 2.0 Client Credentials](https://oauth.net/2/grant-types/client-credentials/)
- [Distributed Caching Best Practices](https://docs.microsoft.com/en-us/azure/architecture/best-practices/caching)

---

**Last Updated:** 2025-01-04
**Version:** 2.0.0
**Status:** Production Ready ✅
