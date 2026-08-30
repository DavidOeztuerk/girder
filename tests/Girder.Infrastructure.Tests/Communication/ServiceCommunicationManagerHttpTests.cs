using Girder.Abstractions.Messaging;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Girder.Infrastructure.Communication;
using Girder.Infrastructure.Communication.Caching;
using Girder.Infrastructure.Communication.Configuration;
using Girder.Infrastructure.Communication.Deduplication;
using Girder.Infrastructure.Communication.Telemetry;
using Girder.Infrastructure.Security.M2M;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute.ExceptionExtensions;

namespace Girder.Infrastructure.Tests.Communication;

/// <summary>
/// Tests for ServiceCommunicationManager HTTP operations via mocked HttpMessageHandler.
/// </summary>
[Trait("Category", "Unit")]
public class ServiceCommunicationManagerHttpTests
{
    private class TestResponse
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private class TestRequest
    {
        public string? Value { get; set; }
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public MockHttpMessageHandler(HttpResponseMessage response)
        {
            _handler = _ => response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private ServiceCommunicationManager CreateManager(
        HttpClient httpClient,
        ServiceCommunicationOptions? options = null,
        Dictionary<string, string?>? configValues = null,
        IServiceResponseCache? cache = null,
        IServiceTokenProvider? tokenProvider = null,
        IServiceCommunicationMetrics? metrics = null,
        IRequestDeduplicator? deduplicator = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();
        var opts = Options.Create(options ?? new ServiceCommunicationOptions { UseGateway = false });
        return new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config,
            options: opts,
            cache: cache,
            tokenProvider: tokenProvider,
            metrics: metrics,
            deduplicator: deduplicator,
            httpContextAccessor: httpContextAccessor);
    }

    #region GetAsync HTTP Tests

    [Fact]
    public async Task GetAsync_SuccessfulApiResponseWrapper_ReturnsUnwrappedData()
    {
        var json = JsonSerializer.Serialize(new
        {
            success = true,
            data = new { id = "123", name = "TestUser" }
        });
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        var result = await manager.GetAsync<TestResponse>("UserService", "/api/users/123");

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("123");
        result.Value!.Name.Should().Be("TestUser");
    }

    [Fact]
    public async Task GetAsync_ServerReturnsNonSuccessStatus_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":\"not found\"}", Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        var result = await manager.GetAsync<TestResponse>("UserService", "/api/users/999");

        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_DirectJsonResponse_ReturnsDeserialized()
    {
        var json = JsonSerializer.Serialize(new TestResponse { Id = "456", Name = "DirectUser" });
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:JobService"] = "http://localhost:8080"
        });

        var result = await manager.GetAsync<TestResponse>("JobService", "/api/jobs/456");

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("456");
        result.Value!.Name.Should().Be("DirectUser");
    }

    [Fact]
    public async Task GetAsync_FailedApiResponse_ReturnsNull()
    {
        var json = JsonSerializer.Serialize(new
        {
            success = false,
            data = (object?)null,
            errors = new[] { "Resource not found" }
        });
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        var result = await manager.GetAsync<TestResponse>("UserService", "/api/users/missing");

        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_HttpRequestException_PropagatesException()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        var act = async () => await manager.GetAsync<TestResponse>("UserService", "/api/users/123");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetAsync_WithMetrics_RecordsServiceCall()
    {
        var metrics = Substitute.For<IServiceCommunicationMetrics>();
        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "1", name = "Test" } });
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            metrics: metrics);

        await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        metrics.Received().RecordServiceCall(
            Arg.Any<string>(), Arg.Any<string>(), "GET",
            Arg.Any<int>(), Arg.Any<TimeSpan>(), fromCache: false);
    }

    #endregion

    #region GetAsync with Deduplication

    [Fact]
    public async Task GetAsync_WithDeduplicationEnabled_CallsDeduplicator()
    {
        var deduplicator = Substitute.For<IRequestDeduplicator>();
        deduplicator.ExecuteAsync(Arg.Any<string>(), Arg.Any<Func<Task<ServiceResponse<TestResponse>?>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var factory = callInfo.ArgAt<Func<Task<ServiceResponse<TestResponse>?>>>(1);
                return factory();
            });

        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "1", name = "Test" } });
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var options = new ServiceCommunicationOptions
        {
            UseGateway = false,
            EnableRequestDeduplication = true
        };
        var manager = CreateManager(httpClient,
            options: options,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            deduplicator: deduplicator);

        await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        await deduplicator.Received(1).ExecuteAsync(
            Arg.Any<string>(),
            Arg.Any<Func<Task<ServiceResponse<TestResponse>?>>>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region GetAsync with Caching

    [Fact]
    public async Task GetAsync_CacheHit_ReturnsCachedResponseWithoutHttpCall()
    {
        var cache = Substitute.For<IServiceResponseCache>();
        var cachedEntry = new CachedResponse<TestResponse>
        {
            Data = new TestResponse { Id = "cached-1", Name = "Cached" },
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        cache.GetAsync<TestResponse>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(cachedEntry);

        var httpCallCount = 0;
        var handler = new MockHttpMessageHandler(_ =>
        {
            httpCallCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var options = new ServiceCommunicationOptions
        {
            UseGateway = false,
            EnableResponseCaching = true
        };
        var manager = CreateManager(httpClient,
            options: options,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            cache: cache);

        var result = await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("cached-1");
        httpCallCount.Should().Be(0, "should not make HTTP call on cache hit");
    }

    [Fact]
    public async Task GetAsync_CacheMiss_FetchesFromServiceAndCaches()
    {
        var cache = Substitute.For<IServiceResponseCache>();
        cache.GetAsync<TestResponse>(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((CachedResponse<TestResponse>?)null);

        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "fresh-1", name = "Fresh" } });
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var options = new ServiceCommunicationOptions
        {
            UseGateway = false,
            EnableResponseCaching = true
        };
        var manager = CreateManager(httpClient,
            options: options,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            cache: cache);

        var result = await manager.GetAsync<TestResponse>("UserService", "/api/users/fresh-1");

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("fresh-1");
        await cache.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Any<TestResponse>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region SendRequestAsync (POST) Tests

    [Fact]
    public async Task SendRequestAsync_SuccessResponse_ReturnsDeserializedData()
    {
        var json = JsonSerializer.Serialize(new
        {
            success = true,
            data = new { id = "new-1", name = "Created" }
        });
        HttpRequestMessage? capturedRequest = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        var result = await manager.SendRequestAsync<TestRequest, TestResponse>(
            "UserService", "/api/users", new TestRequest { Value = "test" });

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("new-1");
        capturedRequest!.Method.Should().Be(HttpMethod.Post);
    }

    [Fact]
    public async Task SendRequestAsync_FailureResponse_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"bad request\"}", Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        var result = await manager.SendRequestAsync<TestRequest, TestResponse>(
            "UserService", "/api/users", new TestRequest { Value = "test" });

        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task SendRequestAsync_UnknownService_ThrowsInvalidOperationException()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient);

        var act = async () => await manager.SendRequestAsync<TestRequest, TestResponse>(
            "UnknownService", "/api/test", new TestRequest());

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*UnknownService*not configured*");
    }

    [Fact]
    public async Task SendRequestAsync_WithCustomHeaders_AttachesHeaders()
    {
        HttpRequestMessage? capturedRequest = null;
        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "1", name = "Test" } });
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient, configValues: new Dictionary<string, string?>
        {
            ["ServiceEndpoints:UserService"] = "http://localhost:8080"
        });

        await manager.SendRequestAsync<TestRequest, TestResponse>(
            "UserService", "/api/users", new TestRequest(),
            headers: new Dictionary<string, string> { ["X-Custom-Header"] = "custom-value" });

        capturedRequest!.Headers.TryGetValues("X-Custom-Header", out var values);
        values.Should().Contain("custom-value");
    }

    #endregion

    #region PublishEventAsync Tests

    [Fact]
    public async Task PublishEventAsync_PublishesEventViaEndpoint()
    {
        var publishEndpoint = Substitute.For<IEventBus>();
        var config = new ConfigurationBuilder().Build();
        var manager = new ServiceCommunicationManager(
            new HttpClient(),
            publishEndpoint,
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var testEvent = new { EventType = "TestEvent", Data = "test-data" };
        await manager.PublishEventAsync(testEvent);

        await publishEndpoint.Received(1).PublishAsync(testEvent, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishEventAsync_PublishThrows_PropagatesException()
    {
        var publishEndpoint = Substitute.For<IEventBus>();
        publishEndpoint
            .PublishAsync(Arg.Any<TestPublishEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Broker unavailable"));

        var config = new ConfigurationBuilder().Build();
        var manager = new ServiceCommunicationManager(
            new HttpClient(),
            publishEndpoint,
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var testPayload = new TestPublishEvent { EventType = "TestEvent" };
        var act = async () => await manager.PublishEventAsync(testPayload);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Broker unavailable*");
    }

    private sealed class TestPublishEvent { public string EventType { get; init; } = string.Empty; }

    #endregion

    #region CheckServiceHealthAsync Tests

    [Fact]
    public async Task CheckServiceHealthAsync_HealthyService_ReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.CheckServiceHealthAsync("userservice");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckServiceHealthAsync_UnhealthyService_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.CheckServiceHealthAsync("userservice");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckServiceHealthAsync_UnknownService_ReturnsFalse()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder().Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.CheckServiceHealthAsync("NonExistentService");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CheckServiceHealthAsync_ConnectionError_ReturnsFalse()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.CheckServiceHealthAsync("userservice");

        result.Should().BeFalse();
    }

    #endregion

    #region DiscoverServiceEndpointsAsync Tests

    [Fact]
    public async Task DiscoverServiceEndpointsAsync_SuccessfulSwaggerDoc_ReturnsEndpoints()
    {
        var swaggerJson = """
        {
            "openapi": "3.0.1",
            "paths": {
                "/api/users": {
                    "get": { "summary": "Get all users", "tags": ["Users"] },
                    "post": { "summary": "Create user", "security": [{}] }
                }
            }
        }
        """;
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(swaggerJson, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.DiscoverServiceEndpointsAsync("userservice");

        result.Should().NotBeEmpty();
        result.Should().ContainKey("GET /api/users");
        result.Should().ContainKey("POST /api/users");
    }

    [Fact]
    public async Task DiscoverServiceEndpointsAsync_SwaggerNotFound_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.DiscoverServiceEndpointsAsync("userservice");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverServiceEndpointsAsync_UnknownService_ReturnsEmpty()
    {
        var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder().Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.DiscoverServiceEndpointsAsync("NonExistentService");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverServiceEndpointsAsync_ConnectionError_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler(_ => throw new HttpRequestException("Connection refused"));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.DiscoverServiceEndpointsAsync("userservice");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverServiceEndpointsAsync_SwaggerDocWithSecurity_RequiresAuthTrue()
    {
        var swaggerJson = """
        {
            "paths": {
                "/api/protected": {
                    "get": { "summary": "Protected endpoint", "security": [{"Bearer": []}] }
                },
                "/api/public": {
                    "get": { "summary": "Public endpoint" }
                }
            }
        }
        """;
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(swaggerJson, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();
        var manager = new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            config);

        var result = await manager.DiscoverServiceEndpointsAsync("userservice");

        result["GET /api/protected"].RequiresAuth.Should().BeTrue();
        result["GET /api/public"].RequiresAuth.Should().BeFalse();
    }

    #endregion

    #region AttachHeadersAsync — M2M and CorrelationId

    [Fact]
    public async Task GetAsync_WithM2MTokenProvider_AttachesBearerToken()
    {
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .Returns("m2m-token-123");

        HttpRequestMessage? capturedRequest = null;
        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "1", name = "Test" } });
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var options = new ServiceCommunicationOptions
        {
            UseGateway = false,
            M2M = new M2MConfiguration { Enabled = true }
        };
        var manager = CreateManager(httpClient,
            options: options,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            tokenProvider: tokenProvider);

        await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        capturedRequest!.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        capturedRequest.Headers.Authorization.Parameter.Should().Be("m2m-token-123");
    }

    [Fact]
    public async Task GetAsync_WithM2MEnabled_TokenProviderFails_ThrowsException()
    {
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Token service unavailable"));

        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var options = new ServiceCommunicationOptions
        {
            UseGateway = false,
            M2M = new M2MConfiguration { Enabled = true }
        };
        var manager = CreateManager(httpClient,
            options: options,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            tokenProvider: tokenProvider);

        var act = async () => await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Token service unavailable*");
    }

    [Fact]
    public async Task GetAsync_WithCorrelationIdInContext_PropagatesCorrelationId()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        var httpContext = Substitute.For<HttpContext>();
        httpContext.Items["CorrelationId"].Returns("test-correlation-id");
        httpContext.TraceIdentifier.Returns("test-trace-id");
        httpContextAccessor.HttpContext.Returns(httpContext);

        HttpRequestMessage? capturedRequest = null;
        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "1", name = "Test" } });
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var manager = CreateManager(httpClient,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            },
            httpContextAccessor: httpContextAccessor);

        await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        capturedRequest!.Headers.TryGetValues("X-Correlation-ID", out var correlationValues);
        correlationValues.Should().Contain("test-correlation-id");
    }

    [Fact]
    public async Task GetAsync_FallbackBearerToken_AttachesFromConfig()
    {
        HttpRequestMessage? capturedRequest = null;
        var json = JsonSerializer.Serialize(new { success = true, data = new { id = "1", name = "Test" } });
        var handler = new MockHttpMessageHandler(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };
        var options = new ServiceCommunicationOptions
        {
            UseGateway = false,
            M2M = new M2MConfiguration { Enabled = false }
        };
        var manager = CreateManager(httpClient,
            options: options,
            configValues: new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080",
                ["ServiceCommunication:BearerToken"] = "fallback-token-xyz"
            });

        await manager.GetAsync<TestResponse>("UserService", "/api/users/1");

        capturedRequest!.Headers.Authorization.Should().NotBeNull();
        capturedRequest.Headers.Authorization!.Parameter.Should().Be("fallback-token-xyz");
    }

    #endregion

    #region GetCacheTtl Tests

    [Fact]
    public void GetCacheTtl_ServiceWithPolicy_ReturnsPolicyTtl()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                DefaultTTL = TimeSpan.FromMinutes(15),
                PerServicePolicies = new Dictionary<string, ServiceCachePolicy>
                {
                    ["userservice"] = new ServiceCachePolicy { TTL = TimeSpan.FromMinutes(30) }
                }
            }
        };
        var manager = new ServiceCommunicationManager(
            new HttpClient(),
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            new ConfigurationBuilder().Build(),
            options: Options.Create(options));

        var ttl = (TimeSpan)typeof(ServiceCommunicationManager)
            .GetMethod("GetCacheTtl", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(manager, ["UserService"])!;

        ttl.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void GetCacheTtl_ServiceWithoutPolicy_ReturnsDefaultTtl()
    {
        var options = new ServiceCommunicationOptions
        {
            EnableResponseCaching = true,
            Caching = new CacheConfiguration
            {
                DefaultTTL = TimeSpan.FromMinutes(10)
            }
        };
        var manager = new ServiceCommunicationManager(
            new HttpClient(),
            Substitute.For<IEventBus>(),
            Substitute.For<ILogger<ServiceCommunicationManager>>(),
            new ConfigurationBuilder().Build(),
            options: Options.Create(options));

        var ttl = (TimeSpan)typeof(ServiceCommunicationManager)
            .GetMethod("GetCacheTtl", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(manager, ["UnknownService"])!;

        ttl.Should().Be(TimeSpan.FromMinutes(10));
    }

    #endregion
}
