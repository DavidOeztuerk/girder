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
        options.EnableEndpointSpecificLimiting.Should().BeTrue();
        options.UseSlidingWindow.Should().BeTrue();
    }

    /// <summary>
    /// Nothing is exempt by default, because an exemption could not be removed.
    /// </summary>
    /// <remarks>
    /// <para>The .NET configuration binder <em>adds</em> to a collection and
    /// never replaces it, and an empty JSON array is indistinguishable from an
    /// absent key. Measured: with <c>"WhitelistedIps": ["9.9.9.9"]</c> in
    /// configuration, the bound value was <c>127.0.0.1, ::1, 9.9.9.9</c>. An
    /// operator who wrote the list out deliberately still got loopback, and
    /// nothing in their own configuration would have told them.</para>
    /// <para>The health probe is protected by <em>order</em> now, not by a path
    /// string: <c>UseHealthCheckEndpoints()</c> runs before
    /// <c>UseRateLimiting()</c>, so it never reaches the limiter.</para>
    /// </remarks>
    [Fact]
    public void Nothing_is_exempt_by_default()
    {
        var options = new DistributedRateLimitingOptions();

        options.WhitelistedIps.Should().BeEmpty();
        options.WhitelistedUserIds.Should().BeEmpty();
        options.WhitelistedEndpoints.Should().BeEmpty();
    }

    /// <summary>
    /// The counter-probe on the reason: configuration really cannot replace.
    /// </summary>
    /// <remarks>
    /// If this ever starts failing, the binder learned to replace collections
    /// and the empty defaults above could be reconsidered.
    /// </remarks>
    [Fact]
    public void Configuration_adds_to_a_collection_and_never_replaces_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RL:WhitelistedIps:0"] = "9.9.9.9"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<DistributedRateLimitingOptions>(configuration.GetSection("RL"));

        var bound = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DistributedRateLimitingOptions>>()
            .Value;

        bound.WhitelistedIps.Should().Equal(
            ["9.9.9.9"],
            "with a non-empty default this would read 127.0.0.1, ::1, 9.9.9.9");
    }

    [Fact]
    /// <summary>
    /// The per-path limits start empty: a library must not carry one
    /// application's route map.
    /// </summary>
    /// <remarks>
    /// It used to arrive holding seven paths from the application Girder was
    /// extracted from, and configuration <em>adds</em> to the dictionary rather
    /// than replacing it — so they could not be removed from outside. Measured, a
    /// call to <c>POST /api/auth/register</c> was refused at three per minute in
    /// an application that has no such route.
    /// </remarks>
    public void EndpointSpecificLimits_StartEmpty()
    {
        new DistributedRateLimitingOptions().EndpointSpecificLimits.Should().BeEmpty();
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
    /// <summary>
    /// The module brings a counter, and a provider package still wins.
    /// </summary>
    /// <remarks>
    /// It used to register none, on the reasoning that where counters live is the
    /// operator's decision. What that actually produced was a default set that
    /// could not start — the pipeline step refused to compose — so the decision
    /// nobody was asked to make was made for them anyway, and badly. The counter
    /// registered here is in-process, the same shape as the framework's own
    /// <c>AddDistributedMemoryCache()</c> that the default set already registers,
    /// and the last registration wins.
    /// </remarks>
    [Fact]
    public void AddDistributedRateLimiting_BringsAnInProcessCounter()
    {
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

        provider.GetService<IDistributedRateLimitStore>()
            .Should().BeOfType<Girder.Infrastructure.RateLimiting.InProcessRateLimitStore>();
    }

    /// <summary>A provider registered afterwards is the one that answers.</summary>
    [Fact]
    public void A_provider_registered_afterwards_replaces_the_built_in_counter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDistributedRateLimiting(new ConfigurationBuilder().Build());

        services.AddInMemoryCache("probe");

        services.BuildServiceProvider().GetService<IDistributedRateLimitStore>()
            .Should().BeOfType<Girder.InMemory.Caching.InMemoryRateLimitStore>(
                "the last registration of a service is the one that wins");
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
