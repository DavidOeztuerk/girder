using Girder.Infrastructure.Security.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// The analysis runs per response; its log must not.
/// </summary>
[Trait("Category", "Unit")]
public class HeaderAnalysisNoiseTests
{
    /// <summary>
    /// RFC 6797 §8.1: a user agent must ignore an HSTS header received over a
    /// non-secure transport. Reporting it missing there asks for a header that
    /// would be discarded.
    /// </summary>
    /// <summary>
    /// A deployment that turned HSTS off gets told about it on every response.
    /// </summary>
    [Fact]
    public async Task Over_plain_http_the_missing_HSTS_header_is_not_a_finding()
    {
        var (host, log) = await StartAsync();
        using (host)
        {
            await host.GetTestClient().GetAsync("/");

            log.Warnings.Should().NotContain(message => message.Contains("Strict-Transport-Security"));
        }
    }

    /// <summary>
    /// The same finding on the tenth request tells the reader nothing the first
    /// one did not, and buries everything else.
    /// </summary>
    [Fact]
    public async Task The_same_finding_is_reported_once()
    {
        var (host, log) = await StartAsync();
        using (host)
        {
            var client = host.GetTestClient();
            for (var i = 0; i < 5; i++)
            {
                await client.GetAsync($"/page-{i}");
            }

            log.Warnings.Count(message => message.Contains("Security headers score"))
                .Should().BeLessThanOrEqualTo(1);
        }
    }

    private static async Task<(IHost Host, CollectingLoggerProvider Log)> StartAsync()
    {
        var log = new CollectingLoggerProvider();

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureLogging(logging => logging.ClearProviders().AddProvider(log))
                .ConfigureServices(services => services.AddSecurityHeaders(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            // What anything serving plain HTTP has to set.
                            ["SecurityHeaders:EnableHsts"] = "false"
                        })
                        .Build()))
                .Configure(app =>
                {
                    app.UseMiddleware<SecurityHeadersMiddleware>();
                    app.Run(context => context.Response.WriteAsync("ok"));
                }))
            .StartAsync();

        return (host, log);
    }
}

/// <summary>Keeps every warning, so a test can count them.</summary>
public sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings
    {
        get { lock (_warnings) { return _warnings.ToArray(); } }
    }

    public ILogger CreateLogger(string categoryName) => new Collector(_warnings);

    public void Dispose() { }

    private sealed class Collector(List<string> warnings) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            lock (warnings)
            {
                warnings.Add(formatter(state, exception));
            }
        }
    }
}
