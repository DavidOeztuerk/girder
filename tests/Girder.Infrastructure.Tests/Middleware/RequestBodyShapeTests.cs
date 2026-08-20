using System.Text;
using Girder.Infrastructure.Middleware;
using Girder.Infrastructure.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Middleware;

/// <summary>
/// When body logging is on, what gets logged is the shape and never a value.
/// </summary>
/// <remarks>
/// Redacting the fields we recognise is enumeration, and enumeration is always
/// incomplete: a case reference, a note to a doctor, a company someone is
/// leaving — none of that is on any list, and all of it went into the log in
/// full. Logging names and lengths instead is safe because of how it is built,
/// not because of what somebody remembered to add.
/// </remarks>
[Trait("Category", "Unit")]
public class RequestBodyShapeTests
{
    [Fact]
    public async Task No_value_survives_however_the_field_is_named()
    {
        var log = await PostAsync("""
            {
              "appointmentNote": "Termin bei Dr. Weber wegen der Kündigung",
              "caseReference": "AZ-2026-4711",
              "amount": 4200
            }
            """);

        log.Should().NotContain("Dr. Weber")
            .And.NotContain("Kündigung")
            .And.NotContain("AZ-2026-4711")
            .And.NotContain("4200");
    }

    /// <summary>
    /// The shape is what a person debugging actually needs: which fields
    /// arrived, and whether they were empty.
    /// </summary>
    [Fact]
    public async Task The_shape_is_kept_so_the_log_is_still_worth_reading()
    {
        var log = await PostAsync("""{"displayName":"Ada Lovelace","email":"ada@example.com"}""");

        log.Should().Contain("displayName").And.Contain("email");
        log.Should().NotContain("Ada Lovelace").And.NotContain("ada@example.com");
    }

    [Fact]
    public async Task A_body_that_is_not_json_is_reduced_to_its_size()
    {
        var log = await PostAsync("Reach me at ada@example.com", "text/plain");

        log.Should().NotContain("ada@example.com");
        log.Should().Contain("27");
    }

    private static async Task<string> PostAsync(string body, string contentType = "application/json")
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

        using var content = new StringContent(body, Encoding.UTF8, contentType);
        await host.GetTestClient().PostAsync("/api/anything", content);

        return string.Join("\n", collector.Entries);
    }
}
