using Girder.Abstractions.Caching;
using Girder.InMemory.Caching;
using Girder.Infrastructure.Caching;
using Girder.Infrastructure.Extensions;
using Girder.Infrastructure.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;


namespace Girder.Infrastructure.Tests.Extensions;

#region DistributedRateLimitingOptions Defaults

[Trait("Category", "Unit")]
public class DistributedRateLimitingOptionsTests
{
    [Fact]
    public void SectionName_IsDistributedRateLimiting()
    {
        DistributedRateLimitingOptions.SectionName.Should().Be("DistributedRateLimiting");
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new DistributedRateLimitingOptions();

        options.Enabled.Should().BeTrue();
        options.RequestsPerMinute.Should().Be(100);
        options.RequestsPerHour.Should().Be(1000);
        options.RequestsPerDay.Should().Be(10000);
        options.EnableIpRateLimiting.Should().BeTrue();
        options.EnableUserRateLimiting.Should().BeTrue();
        options.EnableEndpointSpecificLimiting.Should().BeTrue();
        options.UseSlidingWindow.Should().BeTrue();
    }

    [Fact]
    public void WhitelistedIps_ContainsLoopbackByDefault()
    {
        var options = new DistributedRateLimitingOptions();

        options.WhitelistedIps.Should().Contain("127.0.0.1");
        options.WhitelistedIps.Should().Contain("::1");
    }

    [Fact]
    public void WhitelistedEndpoints_ContainsHealthByDefault()
    {
        var options = new DistributedRateLimitingOptions();

        options.WhitelistedEndpoints.Should().Contain("GET:/health");
        options.WhitelistedEndpoints.Should().Contain("GET:/ready");
        options.WhitelistedEndpoints.Should().Contain("GET:/metrics");
    }

    [Fact]
    public void EndpointSpecificLimits_ContainsDefaultLimits()
    {
        var options = new DistributedRateLimitingOptions();

        options.EndpointSpecificLimits.Should().ContainKey("/api/auth/login");
        options.EndpointSpecificLimits["/api/auth/login"].RequestsPerMinute.Should().Be(5);
    }

    [Fact]
    public void Redis_DefaultConnectionString()
    {
        var options = new DistributedRateLimitingOptions();

        options.Redis.Should().NotBeNull();
        options.Redis.ConnectionString.Should().Be("localhost:6379");
    }

    [Fact]
    public void CircuitBreaker_DefaultValues()
    {
        var options = new DistributedRateLimitingOptions();

        options.CircuitBreaker.Should().NotBeNull();
        options.CircuitBreaker.Enabled.Should().BeTrue();
        options.CircuitBreaker.FailureThreshold.Should().Be(5);
        options.CircuitBreaker.OpenTimeout.Should().Be(TimeSpan.FromSeconds(30));
    }
}

#endregion

#region RedisRateLimitingOptions

[Trait("Category", "Unit")]
public class RedisRateLimitingOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new RedisRateLimitingOptions();

        options.ConnectionString.Should().Be("localhost:6379");
        options.Database.Should().Be(1);
        options.KeyPrefix.Should().Be("rl:");
        options.ConnectTimeout.Should().Be(5000);
        options.CommandTimeout.Should().Be(1000);
        options.RetryCount.Should().Be(3);
        options.UseCluster.Should().BeFalse();
        options.UseSsl.Should().BeFalse();
        options.Password.Should().BeNull();
    }
}

#endregion

#region CircuitBreakerOptions (Rate Limiting)

[Trait("Category", "Unit")]
public class RateLimitCircuitBreakerOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new Girder.Infrastructure.Models.CircuitBreakerOptions();

        options.Enabled.Should().BeTrue();
        options.FailureThreshold.Should().Be(5);
        options.OpenTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.OperationTimeout.Should().Be(TimeSpan.FromSeconds(2));
        options.FallbackBehavior.Should().Be(CircuitBreakerFallback.AllowAll);
    }
}

#endregion

#region CircuitBreakerFallback Enum

[Trait("Category", "Unit")]
public class CircuitBreakerFallbackTests
{
    [Fact]
    public void HasExpectedValues()
    {
        CircuitBreakerFallback.AllowAll.Should().BeDefined();
        CircuitBreakerFallback.DenyAll.Should().BeDefined();
        CircuitBreakerFallback.UseInMemory.Should().BeDefined();
    }
}

#endregion

#region AddDistributedRateLimiting

[Trait("Category", "Unit")]
public class DistributedRateLimitingExtensionMethodTests
{
    [Fact]
    public void AddDistributedRateLimiting_LeavesTheStoreToAProviderPackage()
    {
        // Configuring a rate limit does not decide where the counters live.
        // AddRedisCache() or AddInMemoryCache() does.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DistributedRateLimiting:Enabled", "true" },
                { "DistributedRateLimiting:Redis:ConnectionString", "" }
            })
            .Build();

        services.AddDistributedRateLimiting(configuration);

        var provider = services.BuildServiceProvider();
        var store = provider.GetService<IDistributedRateLimitStore>();
        store.Should().BeNull();
    }

    [Fact]
    public void AddRateLimitingHealthChecks_RegistersHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddRateLimitingHealthChecks();

        var provider = services.BuildServiceProvider();
        var healthCheckOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>();

        healthCheckOptions.Should().NotBeNull();
        healthCheckOptions!.Value.Registrations.Should().Contain(r => r.Name == "rate_limiting");
    }
}

#endregion

#region EndpointRateLimit

[Trait("Category", "Unit")]
public class EndpointRateLimitTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var limit = new EndpointRateLimit();

        // These are value types, so they default to 0
        limit.RequestsPerMinute.Should().Be(0);
        limit.RequestsPerHour.Should().Be(0);
        limit.RequestsPerDay.Should().Be(0);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var limit = new EndpointRateLimit
        {
            RequestsPerMinute = 10,
            RequestsPerHour = 100,
            RequestsPerDay = 1000
        };

        limit.RequestsPerMinute.Should().Be(10);
        limit.RequestsPerHour.Should().Be(100);
        limit.RequestsPerDay.Should().Be(1000);
    }
}

#endregion

#region WindowCheckResult DTO

[Trait("Category", "Unit")]
public class RateLimitResultTests
{
    [Fact]
    public void RemainingRequests_CalculatesCorrectly()
    {
        var result = new WindowCheckResult
        {
            IsAllowed = true,
            CurrentCount = 7,
            Limit = 10
        };

        result.RemainingRequests.Should().Be(3);
    }

    [Fact]
    public void RemainingRequests_NeverNegative()
    {
        var result = new WindowCheckResult
        {
            IsAllowed = false,
            CurrentCount = 15,
            Limit = 10
        };

        result.RemainingRequests.Should().Be(0);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var result = new WindowCheckResult();

        result.IsAllowed.Should().BeFalse();
        result.CurrentCount.Should().Be(0);
        result.Limit.Should().Be(0);
        result.ResetTime.Should().BeNull();
    }
}

#endregion
