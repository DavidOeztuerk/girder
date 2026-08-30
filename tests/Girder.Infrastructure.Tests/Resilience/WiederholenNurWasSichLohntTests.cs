using System.Net;
using Girder.Infrastructure.Resilience;

namespace Girder.Infrastructure.Tests.Resilience;

/// <summary>
/// What a retry is for, and what it is not for.
/// </summary>
/// <remarks>
/// A status code is an answer. Asking a second time gets the same one, costs the
/// far service three times the work, and reaches the caller as a transport
/// failure rather than as the answer it was.
/// </remarks>
[Trait("Category", "Unit")]
public class WiederholenNurWasSichLohntTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public async Task Eine_Antwort_wird_einmal_geholt(HttpStatusCode status)
    {
        var zaehler = new ZaehlenderHandler(status);
        using var client = Client(zaehler);

        var antwort = await client.GetAsync(new Uri("http://dienst/etwas"));

        antwort.StatusCode.Should().Be(status);
        zaehler.Versuche.Should().Be(1, "asking again gets the same answer");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Was_voruebergehen_kann_wird_wiederholt(HttpStatusCode status)
    {
        var zaehler = new ZaehlenderHandler(status);
        using var client = Client(zaehler);

        await client.GetAsync(new Uri("http://dienst/etwas"));

        zaehler.Versuche.Should().Be(3);
    }

    /// <summary>
    /// The finding as it was reported: a 404 arrived at the caller as a thrown
    /// transport failure, three requests later.
    /// </summary>
    [Fact]
    public async Task Ein_404_kommt_als_404_an_und_nicht_als_Ausnahme()
    {
        var zaehler = new ZaehlenderHandler(HttpStatusCode.NotFound);
        using var client = Client(zaehler);

        var antwort = await client.GetAsync(new Uri("http://dienst/fehlt"));

        antwort.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// A connection that never answered is the case retries exist for.
    /// </summary>
    [Fact]
    public async Task Ein_Verbindungsfehler_wird_weiter_wiederholt()
    {
        var zaehler = new ZaehlenderHandler(new HttpRequestException("connection refused"));
        using var client = Client(zaehler);

        var versuch = async () => await client.GetAsync(new Uri("http://dienst/etwas"));

        await versuch.Should().ThrowAsync<HttpRequestException>();
        zaehler.Versuche.Should().Be(3);
    }

    /// <summary>
    /// A retried request must be a second request, not the same one sent twice —
    /// an HttpRequestMessage cannot be sent again.
    /// </summary>
    [Fact]
    public async Task Eine_Wiederholung_schickt_eine_neue_Nachricht_mit_dem_Inhalt()
    {
        var zaehler = new ZaehlenderHandler(HttpStatusCode.ServiceUnavailable);
        using var client = Client(zaehler);

        using var anfrage = new HttpRequestMessage(HttpMethod.Post, new Uri("http://dienst/etwas"))
        {
            Content = new StringContent("nutzlast")
        };

        await client.SendAsync(anfrage);

        zaehler.Versuche.Should().Be(3);
        zaehler.Inhalte.Should().AllSatisfy(inhalt => inhalt.Should().Be("nutzlast"));
    }

    private static HttpClient Client(HttpMessageHandler inner) =>
        new(new ResilientHttpPolicyHandler(new DurchreichenderSchalter(), new DreiVersuche())
        {
            InnerHandler = inner
        });

    /// <summary>Counts what actually went out, and answers the same thing every time.</summary>
    private sealed class ZaehlenderHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;
        private readonly Exception? _fehler;

        public ZaehlenderHandler(HttpStatusCode status) => _status = status;
        public ZaehlenderHandler(Exception fehler) => _fehler = fehler;

        public int Versuche { get; private set; }
        public List<string> Inhalte { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Versuche++;

            if (request.Content is not null)
            {
                Inhalte.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _fehler is not null
                ? throw _fehler
                : new HttpResponseMessage(_status!.Value);
        }
    }

    /// <summary>A closed circuit: the question here is the retry, not the breaker.</summary>
    private sealed class DurchreichenderSchalter : ICircuitBreaker
    {
        public CircuitBreakerState State => CircuitBreakerState.Closed;
        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default) => operation();
        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>> fallback, CancellationToken cancellationToken = default) => operation();
        public Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default) => operation();
        public Task ExecuteAsync(Func<Task> operation, Func<Task> fallback, CancellationToken cancellationToken = default) => operation();
        public CircuitBreakerStatistics GetStatistics() => new();
        public void Reset() { }
        public void ForceOpen() { }
    }

    /// <summary>Three attempts, no waiting — the count is what is being measured.</summary>
    private sealed class DreiVersuche : IRetryPolicy
    {
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
        {
            for (var versuch = 1; ; versuch++)
            {
                try
                {
                    return await operation();
                }
                catch when (versuch < 3)
                {
                    // and round again
                }
            }
        }

        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Exception, bool> shouldRetry, CancellationToken cancellationToken = default) =>
            ExecuteAsync(operation, cancellationToken);
        public Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Action<int, Exception, TimeSpan> onRetry, CancellationToken cancellationToken = default) =>
            ExecuteAsync(operation, cancellationToken);
        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default) => await operation();
        public RetryPolicyStatistics GetStatistics() => new();
        public void ResetStatistics() { }
    }
}
