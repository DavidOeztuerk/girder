using Girder.Infrastructure.Resilience;
using System.Net;

namespace Girder.Infrastructure.Tests.Resilience;

[Trait("Category", "Unit")]
public class ResilientHttpPolicyHandlerTests
{
    private class TestHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    private static (ResilientHttpPolicyHandler handler, HttpClient client) CreateHandlerAndClient(
        ICircuitBreaker? circuitBreaker = null,
        IRetryPolicy? retryPolicy = null,
        Func<HttpRequestMessage, HttpResponseMessage>? innerHandler = null)
    {
        circuitBreaker ??= Substitute.For<ICircuitBreaker>();
        retryPolicy ??= Substitute.For<IRetryPolicy>();

        // Default circuit breaker: just executes the operation
        circuitBreaker.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<HttpResponseMessage>>>()());

        // Default retry policy: just executes the operation
        retryPolicy.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<HttpResponseMessage>>>()());

        var handler = new ResilientHttpPolicyHandler(circuitBreaker, retryPolicy)
        {
            InnerHandler = innerHandler != null
                ? new TestHttpHandler(innerHandler)
                : new TestHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        };

        var client = new HttpClient(handler);
        return (handler, client);
    }

    [Fact]
    public async Task SendAsync_WhenRequestSucceeds_ReturnsSuccessResponse()
    {
        var (_, client) = CreateHandlerAndClient(
            innerHandler: _ => new HttpResponseMessage(HttpStatusCode.OK));

        var response = await client.GetAsync("http://localhost/api/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_WhenInnerHandlerFails_CircuitBreakerAndRetryAreCalled()
    {
        var circuitBreaker = Substitute.For<ICircuitBreaker>();
        var retryPolicy = Substitute.For<IRetryPolicy>();
        var callCount = 0;

        circuitBreaker.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callInfo.Arg<Func<Task<HttpResponseMessage>>>()();
            });

        retryPolicy.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<HttpResponseMessage>>>()());

        var handler = new ResilientHttpPolicyHandler(circuitBreaker, retryPolicy)
        {
            InnerHandler = new TestHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        };

        var client = new HttpClient(handler);
        await client.GetAsync("http://localhost/api/test");

        callCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_WhenResponseIsNotSuccess_ThrowsHttpRequestException()
    {
        var circuitBreaker = Substitute.For<ICircuitBreaker>();
        var retryPolicy = Substitute.For<IRetryPolicy>();

        circuitBreaker.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<HttpResponseMessage>>>()());

        // Retry policy propagates exception
        retryPolicy.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<HttpResponseMessage>>>()());

        var handler = new ResilientHttpPolicyHandler(circuitBreaker, retryPolicy)
        {
            InnerHandler = new TestHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))
        };

        var client = new HttpClient(handler);
        var act = async () => await client.GetAsync("http://localhost/api/test");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SendAsync_WhenCircuitBreakerThrows_PropagatesException()
    {
        var circuitBreaker = Substitute.For<ICircuitBreaker>();
        var retryPolicy = Substitute.For<IRetryPolicy>();

        circuitBreaker.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Circuit is open"));

        retryPolicy.ExecuteAsync(Arg.Any<Func<Task<HttpResponseMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => callInfo.Arg<Func<Task<HttpResponseMessage>>>()());

        var handler = new ResilientHttpPolicyHandler(circuitBreaker, retryPolicy)
        {
            InnerHandler = new TestHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))
        };

        var client = new HttpClient(handler);
        var act = async () => await client.GetAsync("http://localhost/api/test");

        await act.Should().ThrowAsync<Exception>().WithMessage("Circuit is open");
    }
}
