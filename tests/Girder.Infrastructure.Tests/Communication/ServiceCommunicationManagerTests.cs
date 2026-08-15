using System.Reflection;
using Girder.Infrastructure.Communication;
using Girder.Infrastructure.Communication.Configuration;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Communication;

[Trait("Category", "Unit")]
public class ServiceCommunicationManagerTests
{
    private class TestDto
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }

    private ServiceCommunicationManager CreateManager(
        ServiceCommunicationOptions? options = null,
        Dictionary<string, string?>? configValues = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();
        var opts = Options.Create(options ?? new ServiceCommunicationOptions());
        return new ServiceCommunicationManager(
            new HttpClient(),
            Substitute.For<IPublishEndpoint>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config,
            options: opts);
    }

    private TResult? InvokeUnwrapResponse<TResult>(ServiceCommunicationManager manager, string responseContent, string serviceName)
        where TResult : class
    {
        var method = typeof(ServiceCommunicationManager)
            .GetMethod("UnwrapResponse", BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(typeof(TResult));

        return (TResult?)method.Invoke(manager, new object[] { responseContent, serviceName });
    }

    private bool InvokeIsCacheable(ServiceCommunicationManager manager, string serviceName, string endpoint, string method)
    {
        var methodInfo = typeof(ServiceCommunicationManager)
            .GetMethod("IsCacheable", BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (bool)methodInfo.Invoke(manager, new object[] { serviceName, endpoint, method })!;
    }

    private bool InvokeShouldRetryForServiceCall(ServiceCommunicationManager manager, Exception exception)
    {
        var method = typeof(ServiceCommunicationManager)
            .GetMethod("ShouldRetryForServiceCall", BindingFlags.NonPublic | BindingFlags.Instance)!;

        return (bool)method.Invoke(manager, new object[] { exception })!;
    }

    #region UnwrapResponse Tests

    [Fact]
    public void UnwrapResponse_WithSuccessApiResponse_UnwrapsDataObject()
    {
        var manager = CreateManager();
        var json = """{"success": true, "data": {"name": "Test", "value": 42}}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void UnwrapResponse_WithFailedApiResponse_WithDataProperty_ReturnsNull()
    {
        var manager = CreateManager();
        // Must have both "success" and "data" properties for the ApiResponse path
        var json = """{"success": false, "data": {"name": "test"}, "errors": ["Something went wrong"]}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        result.Should().BeNull("the code returns default when success is false");
    }

    [Fact]
    public void UnwrapResponse_WithFailedApiResponse_WithoutDataProperty_FallsThrough()
    {
        var manager = CreateManager();
        // Without "data" property, the ApiResponse check is skipped entirely
        var json = """{"success": false, "errors": ["Something went wrong"]}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        // Falls through to direct deserialization which returns a default TestDto
        result.Should().NotBeNull("without 'data' property, it falls through to direct deserialization");
    }

    [Fact]
    public void UnwrapResponse_WithNullDataInApiResponse_FallsThroughToDirectDeserialization()
    {
        var manager = CreateManager();
        // When data is null, the ApiResponse unwrap is skipped (ValueKind == Null)
        // and success is true, so it falls through to direct deserialization
        var json = """{"success": true, "data": null}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        // Direct deserialization of the full JSON produces a default TestDto
        result.Should().NotBeNull("falls through to direct deserialization");
    }

    [Fact]
    public void UnwrapResponse_WithNonApiResponseJson_DeserializesDirectly()
    {
        var manager = CreateManager();
        var json = """{"name": "Direct", "value": 99}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Direct");
        result.Value.Should().Be(99);
    }

    [Fact]
    public void UnwrapResponse_WithInvalidJson_ReturnsNull()
    {
        var manager = CreateManager();
        var json = "this is not json at all";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        result.Should().BeNull();
    }

    [Fact]
    public void UnwrapResponse_WithEmptyDataObject_ReturnsDefaultDto()
    {
        var manager = CreateManager();
        var json = """{"success": true, "data": {}}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        result.Should().NotBeNull();
        result!.Name.Should().BeNull();
        result.Value.Should().Be(0);
    }

    [Fact]
    public void UnwrapResponse_WithSuccessFalseAndNoErrors_ReturnsNull()
    {
        var manager = CreateManager();
        var json = """{"success": false, "data": {"name": "Ignored", "value": 1}}""";

        var result = InvokeUnwrapResponse<TestDto>(manager, json, "TestService");

        result.Should().BeNull();
    }

    #endregion

    #region IsCacheable Tests

    [Fact]
    public void IsCacheable_CachingDisabledGlobally_ReturnsFalse()
    {
        var options = new ServiceCommunicationOptions { EnableResponseCaching = false };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "UserService", "/api/users", "GET");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsCacheable_NoPerServicePolicy_GetMethod_ReturnsTrue()
    {
        var options = new ServiceCommunicationOptions { EnableResponseCaching = true };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "UserService", "/api/users", "GET");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsCacheable_NoPerServicePolicy_PostMethod_ReturnsFalse()
    {
        var options = new ServiceCommunicationOptions { EnableResponseCaching = true };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "UserService", "/api/users", "POST");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsCacheable_ServicePolicyDisabled_ReturnsFalse()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                PerServicePolicies = new Dictionary<string, ServiceCachePolicy>
                {
                    ["userservice"] = new ServiceCachePolicy { Enabled = false }
                }
            }
        };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "UserService", "/api/users", "GET");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsCacheable_ServicePolicy_NonCacheableMethod_ReturnsFalse()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                PerServicePolicies = new Dictionary<string, ServiceCachePolicy>
                {
                    ["userservice"] = new ServiceCachePolicy
                    {
                        Enabled = true,
                        CacheableMethods = new HashSet<string> { "GET" }
                    }
                }
            }
        };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "UserService", "/api/users", "POST");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsCacheable_ExcludePatternMatches_ReturnsFalse()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                PerServicePolicies = new Dictionary<string, ServiceCachePolicy>
                {
                    ["userservice"] = new ServiceCachePolicy
                    {
                        Enabled = true,
                        CacheableMethods = new HashSet<string> { "GET" },
                        ExcludePatterns = new HashSet<string> { "/api/users/me" }
                    }
                }
            }
        };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "UserService", "/api/users/me", "GET");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsCacheable_IncludePatternsDefined_EndpointMatches_ReturnsTrue()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                PerServicePolicies = new Dictionary<string, ServiceCachePolicy>
                {
                    ["jobservice"] = new ServiceCachePolicy
                    {
                        Enabled = true,
                        CacheableMethods = new HashSet<string> { "GET" },
                        IncludePatterns = new HashSet<string> { "/api/jobs.*" }
                    }
                }
            }
        };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "JobService", "/api/jobs/123", "GET");

        result.Should().BeTrue();
    }

    [Fact]
    public void IsCacheable_IncludePatternsDefined_EndpointDoesNotMatch_ReturnsFalse()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                PerServicePolicies = new Dictionary<string, ServiceCachePolicy>
                {
                    ["jobservice"] = new ServiceCachePolicy
                    {
                        Enabled = true,
                        CacheableMethods = new HashSet<string> { "GET" },
                        IncludePatterns = new HashSet<string> { "/api/jobs.*" }
                    }
                }
            }
        };
        var manager = CreateManager(options);

        var result = InvokeIsCacheable(manager, "JobService", "/api/categories", "GET");

        result.Should().BeFalse();
    }

    #endregion

    #region ShouldRetryForServiceCall Tests

    [Fact]
    public void ShouldRetryForServiceCall_HttpRequestException_ReturnsTrue()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new HttpRequestException("Connection refused"));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryForServiceCall_TaskCanceledException_ReturnsTrue()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new TaskCanceledException("Request timed out"));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryForServiceCall_TimeoutException_ReturnsTrue()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new TimeoutException("Operation timed out"));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryForServiceCall_ArgumentException_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new ArgumentException("Invalid parameter"));

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetryForServiceCall_ExceptionWith503InMessage_ReturnsTrue()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new Exception("Service returned 503 Service Unavailable"));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryForServiceCall_ExceptionWith429InMessage_ReturnsTrue()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new Exception("Rate limited: 429 Too Many Requests"));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryForServiceCall_ExceptionWith400InMessage_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new Exception("Bad request: 400"));

        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetryForServiceCall_ExceptionWith500InMessage_ReturnsTrue()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new Exception("Internal server error: 500"));

        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetryForServiceCall_GenericExceptionWithoutStatusCode_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = InvokeShouldRetryForServiceCall(manager, new InvalidOperationException("Some random error"));

        result.Should().BeFalse();
    }

    #endregion

    #region InitializeServiceUrls Tests

    private static Dictionary<string, string> ServiceUrlsOf(ServiceCommunicationManager manager) =>
        (Dictionary<string, string>)typeof(ServiceCommunicationManager)
            .GetField("_serviceUrls", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(manager)!;

    [Fact]
    public void Constructor_WithNoConfig_KnowsNoService()
    {
        // No defaults: an unconfigured service has to fail at the call. A
        // default pointing at localhost would turn that into a silent call to
        // whatever happens to listen there.
        var manager = CreateManager();

        ServiceUrlsOf(manager).Should().BeEmpty();
    }

    [Fact]
    public void Constructor_ReadsEveryConfiguredEndpoint()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://custom-user:9001",
            ["ServiceEndpoints:AnythingElse"] = "http://custom-anything:9002",
            ["ServiceEndpoints:Gateway"] = "http://custom-gateway:9080"
        };

        var manager = CreateManager(configValues: configValues);
        var serviceUrls = ServiceUrlsOf(manager);

        serviceUrls["userservice"].Should().Be("http://custom-user:9001");
        serviceUrls["anythingelse"].Should().Be("http://custom-anything:9002");
        serviceUrls["gateway"].Should().Be("http://custom-gateway:9080");
        serviceUrls.Should().HaveCount(3);
    }

    [Fact]
    public void Constructor_IgnoresAnEmptyEndpoint()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "  "
        };

        var manager = CreateManager(configValues: configValues);

        ServiceUrlsOf(manager).Should().BeEmpty();
    }

    #endregion

    #region GetAsync Behavior Tests

    [Fact]
    public async Task GetAsync_UnknownServiceName_ThrowsInvalidOperationException()
    {
        var options = new ServiceCommunicationOptions { UseGateway = false };
        var manager = CreateManager(options);

        var act = async () => await manager.GetAsync<TestDto>("NonExistentService", "/api/test");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*NonExistentService*not configured*");
    }

    [Fact]
    public async Task GetAsync_UnknownServiceWithConfiguredGateway_ResolvesViaTheGateway()
    {
        // With UseGateway every request goes to the gateway, so the peer's own
        // name never has to be configured — but the gateway does.
        var options = new ServiceCommunicationOptions { UseGateway = true };
        var configValues = new Dictionary<string, string?>
        {
            ["ServiceEndpoints:Gateway"] = "http://gateway.internal:8080"
        };
        var manager = CreateManager(options, configValues: configValues);

        try
        {
            await manager.GetAsync<TestDto>("AnyServiceName", "/api/test");
        }
        catch (Exception ex)
        {
            // Anything from the network is fine; "not configured" is not.
            ex.Should().NotBeOfType<System.InvalidOperationException>();
        }
    }

    [Fact]
    public async Task GetAsync_WithGatewayButNoGatewayConfigured_Throws()
    {
        var options = new ServiceCommunicationOptions { UseGateway = true };
        var manager = CreateManager(options);

        var act = () => manager.GetAsync<TestDto>("AnyServiceName", "/api/test");

        await act.Should().ThrowAsync<System.InvalidOperationException>();
    }

    #endregion
}
