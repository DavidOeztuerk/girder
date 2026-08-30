using System.Net;
using System.Text;
using Girder.Abstractions.Messaging;
using Girder.Infrastructure.Communication;
using Girder.Infrastructure.Communication.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Communication;

/// <summary>
/// What a failed call to another service leaves in this service's log.
/// </summary>
/// <remarks>
/// The body of a foreign answer is that service's data, and it lands here
/// verbatim: a name, an address, whatever the other side happened to put in its
/// error. Since the body now reaches the caller in
/// <see cref="ServiceResponse{T}.Body"/>, copying it into the log adds nothing
/// and takes on a retention obligation for data this service was never given.
/// <para>
/// The shape belongs in the log — which service, which status, how many errors.
/// The values belong to the caller, who knows whether they may be kept.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
public class FremdeRuempfeBleibenDraussenTests
{
    private const string Geheimnis = "anna.beispiel@example.com hat kein Konto";

    private sealed class Benutzer
    {
        public string? Id { get; set; }
    }

    [Fact]
    public async Task Ein_fehlgeschlagener_GET_schreibt_den_Rumpf_nicht_ins_Protokoll()
    {
        var protokoll = new Mitschrift();

        await Manager(protokoll, HttpStatusCode.NotFound, $$"""{"error":"{{Geheimnis}}"}""")
            .GetAsync<Benutzer>("UserService", "/api/users/999");

        protokoll.Alles.Should().NotContain(Geheimnis);
    }

    [Fact]
    public async Task Ein_fehlgeschlagener_POST_schreibt_den_Rumpf_nicht_ins_Protokoll()
    {
        var protokoll = new Mitschrift();

        await Manager(protokoll, HttpStatusCode.UnprocessableEntity, $$"""{"error":"{{Geheimnis}}"}""")
            .SendRequestAsync<Benutzer, Benutzer>("UserService", "/api/users", new Benutzer());

        protokoll.Alles.Should().NotContain(Geheimnis);
    }

    /// <summary>
    /// The envelope's own error list is foreign content too, and it arrives on a
    /// 200 — the path that is easiest to forget.
    /// </summary>
    [Fact]
    public async Task Fehlermeldungen_aus_dem_fremden_Umschlag_bleiben_draussen()
    {
        var protokoll = new Mitschrift();

        // Both "success" and "data" have to be present, or the unwrapper does not
        // recognise the envelope and never reaches the branch under test.
        await Manager(protokoll, HttpStatusCode.OK,
                $$"""{"success":false,"data":null,"errors":["{{Geheimnis}}"]}""")
            .GetAsync<Benutzer>("UserService", "/api/users/1");

        protokoll.Alles.Should().NotContain(Geheimnis);
    }

    /// <summary>
    /// And it is still there for whoever may decide what to do with it.
    /// </summary>
    [Fact]
    public async Task Der_Rumpf_kommt_weiterhin_beim_Aufrufer_an()
    {
        var antwort = await Manager(new Mitschrift(), HttpStatusCode.NotFound,
                $$"""{"error":"{{Geheimnis}}"}""")
            .GetAsync<Benutzer>("UserService", "/api/users/999");

        antwort.Body.Should().Contain(Geheimnis);
    }

    /// <summary>
    /// Removing the body must not remove the entry: which service answered what
    /// is the part that belongs in a log.
    /// </summary>
    [Fact]
    public async Task Dienst_und_Statuscode_stehen_weiterhin_im_Protokoll()
    {
        var protokoll = new Mitschrift();

        await Manager(protokoll, HttpStatusCode.NotFound, $$"""{"error":"{{Geheimnis}}"}""")
            .GetAsync<Benutzer>("UserService", "/api/users/999");

        protokoll.Alles.Should().Contain("UserService").And.Contain("NotFound");
    }

    private static ServiceCommunicationManager Manager(
        ILogger<ServiceCommunicationManager> protokoll,
        HttpStatusCode status,
        string rumpf)
    {
        var httpClient = new HttpClient(new FesteAntwort(status, rumpf))
        {
            BaseAddress = new Uri("http://localhost:8080")
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();

        return new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            protokoll,
            config,
            options: Options.Create(new ServiceCommunicationOptions { UseGateway = false }));
    }

    private sealed class FesteAntwort(HttpStatusCode status, string rumpf) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(rumpf, Encoding.UTF8, "application/json")
            });
    }

    /// <summary>Everything that was written, formatted the way a sink would see it.</summary>
    private sealed class Mitschrift : ILogger<ServiceCommunicationManager>
    {
        private readonly List<string> _zeilen = [];

        public string Alles => string.Join("\n", _zeilen);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _zeilen.Add(formatter(state, exception));

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    }
}
