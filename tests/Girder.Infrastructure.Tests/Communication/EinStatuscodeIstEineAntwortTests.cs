using System.Net;
using System.Text;
using System.Text.Json;
using Girder.Abstractions.Messaging;
using Girder.Infrastructure.Communication;
using Girder.Infrastructure.Communication.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Communication;

/// <summary>
/// What the caller learns about a call that did not succeed.
/// </summary>
/// <remarks>
/// Mapping every non-2xx onto null makes "the user does not exist", "the user
/// exists and has no name" and "the far service is broken" one value. The
/// caller then has to guess, and the guess is usually "retry", which is wrong
/// for two of the three.
/// </remarks>
[Trait("Category", "Unit")]
public class EinStatuscodeIstEineAntwortTests
{
    private sealed class Benutzer
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    [Fact]
    public async Task Ein_404_kommt_als_404_beim_Aufrufer_an()
    {
        var manager = Manager(HttpStatusCode.NotFound, """{"error":"no such user"}""");

        var antwort = await manager.GetAsync<Benutzer>("UserService", "/api/users/999");

        antwort.Status.Should().Be(HttpStatusCode.NotFound);
        antwort.IsSuccess.Should().BeFalse();
        antwort.Value.Should().BeNull();
    }

    /// <summary>
    /// The distinction the old shape could not make.
    /// </summary>
    [Fact]
    public async Task Ein_leerer_Erfolg_ist_nicht_dasselbe_wie_ein_404()
    {
        var leer = await Manager(HttpStatusCode.NoContent, "").GetAsync<Benutzer>("UserService", "/api/users/1");
        var fehlt = await Manager(HttpStatusCode.NotFound, "").GetAsync<Benutzer>("UserService", "/api/users/2");

        leer.IsSuccess.Should().BeTrue();
        fehlt.IsSuccess.Should().BeFalse();
    }

    /// <summary>
    /// The far service said why. Throwing that away leaves the near one with
    /// nothing to log and nothing to pass on.
    /// </summary>
    [Fact]
    public async Task Der_Rumpf_der_Fehlerantwort_bleibt_lesbar()
    {
        var manager = Manager(HttpStatusCode.UnprocessableEntity, """{"error":"name is required"}""");

        var antwort = await manager.SendRequestAsync<Benutzer, Benutzer>(
            "UserService", "/api/users", new Benutzer());

        antwort.Body.Should().Contain("name is required");
    }

    [Fact]
    public async Task Ein_500_ist_ein_500_und_keine_Ausnahme()
    {
        var manager = Manager(HttpStatusCode.InternalServerError, "boom");

        var antwort = await manager.GetAsync<Benutzer>("UserService", "/api/users/1");

        antwort.Status.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Ein_Erfolg_traegt_weiterhin_den_Wert()
    {
        var manager = Manager(HttpStatusCode.OK,
            JsonSerializer.Serialize(new { id = "123", name = "Anna" }));

        var antwort = await manager.GetAsync<Benutzer>("UserService", "/api/users/123");

        antwort.IsSuccess.Should().BeTrue();
        antwort.Value!.Name.Should().Be("Anna");
    }

    private static ServiceCommunicationManager Manager(HttpStatusCode status, string body)
    {
        var handler = new FesteAntwort(status, body);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080") };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceEndpoints:UserService"] = "http://localhost:8080"
            })
            .Build();

        return new ServiceCommunicationManager(
            httpClient,
            Substitute.For<IEventBus>(),
            NullLogger<ServiceCommunicationManager>.Instance,
            config,
            options: Options.Create(new ServiceCommunicationOptions { UseGateway = false }));
    }

    private sealed class FesteAntwort(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
