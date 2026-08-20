using System.Text;
using Girder.Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Middleware;

/// <summary>
/// What survives in the HTTP log when body logging is on.
/// </summary>
/// <remarks>
/// Off by default, and this is why: a request body is where a person's name,
/// address and password all arrive at once. When an operator turns it on to
/// chase a problem, the redaction is the only thing standing between that and
/// wherever logs are shipped.
/// </remarks>
[Trait("Category", "Unit")]
public class RequestBodyRedactionTests
{
    [Fact]
    public async Task A_registration_body_leaves_nothing_behind()
    {
        var log = await PostAsync(new
        {
            displayName = "Ada Lovelace",
            email = "ada@example.com",
            password = "hunter2-and-then-some"
        });

        log.Should().NotContain("hunter2")
            .And.NotContain("ada@example.com")
            .And.NotContain("Ada Lovelace");
    }

    /// <summary>
    /// A body with no password in it is the case that used to pass through
    /// whole — nothing in it matched the keyword list.
    /// </summary>
    [Fact]
    public async Task A_profile_body_loses_the_person_and_keeps_the_rest()
    {
        var log = await PostAsync(new
        {
            displayName = "Ada Lovelace",
            city = "London",
            biography = "Worked on the Analytical Engine."
        });

        log.Should().NotContain("Ada Lovelace").And.NotContain("London");
        log.Should().Contain("Analytical Engine", "a log that says nothing is not worth keeping");
    }

    [Fact]
    public async Task A_payout_body_loses_the_account()
    {
        var log = await PostAsync(new { iban = "DE89370400440532013000", amount = 4200 });

        log.Should().NotContain("DE89370400440532013000");
        log.Should().Contain("4200");
    }

    /// <summary>
    /// An address typed into a free-text field is still an address.
    /// </summary>
    /// <remarks>
    /// Found by running it: a todo whose title contained an email address went
    /// into the log in full, because <c>title</c> is not a sensitive field
    /// name. Field names cannot catch what a person types.
    /// </remarks>
    [Fact]
    public async Task An_address_typed_into_free_text_is_caught()
    {
        var log = await PostAsync(new { title = "Reach me at ada@example.com any time" });

        log.Should().NotContain("ada@example.com");
        log.Should().Contain("Reach me at", "only the address is the problem");
    }

    [Fact]
    public async Task So_is_a_card_number()
    {
        var log = await PostAsync(new { note = "Charged 4111 1111 1111 1111 yesterday" });

        log.Should().NotContain("4111 1111 1111 1111");
    }

    private static async Task<string> PostAsync(object body)
    {
        var collector = new CollectingLoggerProvider();

        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureLogging(logging => logging
                    .ClearProviders()
                    .AddProvider(collector)
                    .SetMinimumLevel(LogLevel.Debug))
                .ConfigureServices(services => services
                    .Configure<Girder.Infrastructure.Observability.ObservabilityOptions>(
                        options => options.EnableDetailedHttpLogging = true))
                .Configure(app =>
                {
                    app.UseMiddleware<RequestLoggingMiddleware>();
                    app.Run(context => context.Response.WriteAsync("ok"));
                }))
            .StartAsync();

        // Explicit content, so Content-Length is set — the middleware reads the
        // body only when it knows how long it is.
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        await host.GetTestClient().PostAsync("/api/profile", content);

        return string.Join("\n", collector.Entries);
    }
}

/// <summary>Keeps every log entry, so a test can look for what should not be there.</summary>
public sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get { lock (_entries) { return _entries.ToArray(); } }
    }

    public ILogger CreateLogger(string categoryName) => new Collector(_entries);

    public void Dispose() { }

    private sealed class Collector(List<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = new StringBuilder(formatter(state, exception));

            // Structured values carry the body too, and a test that only read
            // the message would miss it.
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var value in values)
                {
                    line.Append(' ').Append(value.Value);
                }
            }

            lock (entries)
            {
                entries.Add(line.ToString());
            }
        }
    }
}
