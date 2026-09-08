using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Exceptions;
using Girder.Core.Exceptions;

namespace Girder.Infrastructure.Logging;

/// <summary>
/// Builds the global Serilog logger: log levels, enrichment, noise filtering
/// and exception shaping.
/// </summary>
public static class LoggingConfiguration
{
    /// <summary>
    /// Assigns <see cref="Log.Logger"/>. Call once at startup, before the host
    /// is built.
    /// </summary>
    /// <param name="configuration">Supplies the <c>Serilog</c> section.</param>
    /// <param name="environment">Decides console formatting and the log path.</param>
    /// <param name="serviceName">Attached to every event as <c>ServiceName</c>.</param>
    /// <remarks>
    /// Sinks come from the <c>Serilog:WriteTo</c> configuration section. Declaring
    /// even one there replaces the built-in console and file sinks completely, so
    /// list every destination you want. Install the sink's NuGet package alongside
    /// naming it — Girder ships none.
    /// </remarks>
    public static void ConfigureSerilog(IConfiguration configuration, IHostEnvironment environment, string serviceName)
    {
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithProperty("Environment", environment.EnvironmentName)
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithExceptionDetails() // Enhanced exception details
            .Enrich.WithDataMasking()
            .Filter.ByExcluding(Matching.FromSource("Microsoft.AspNetCore.StaticFiles"))
            .Filter.ByExcluding(Matching.WithProperty<string>("RequestPath", path => 
                path.StartsWith("/health") || path.StartsWith("/metrics")));

        // Custom exception destructuring for better control
        loggerConfig.Destructure.ByTransforming<DomainException>(ex => new
        {
            ex.ErrorCode,
            ex.Message,
            ex.Details,
            ex.AdditionalData,
            Type = ex.GetType().Name
            // Omit stack trace for domain exceptions in all environments
        });

        // Different log levels for different environments
        if (environment.IsDevelopment())
        {
            loggerConfig.MinimumLevel.Debug();
        }
        else if (environment.IsProduction())
        {
            loggerConfig.MinimumLevel.Information();
        }

        // Sinks the application declared. It installs the sink package it wants
        // — OpenSearch, Seq, syslog — and names it here. Girder pins none.
        loggerConfig.ReadFrom.Configuration(configuration);

        if (!HasConfiguredSinks(configuration))
        {
            ApplyDefaultSinks(loggerConfig, environment, serviceName);
        }

        Log.Logger = loggerConfig.CreateLogger();
    }

    private static bool HasConfiguredSinks(IConfiguration configuration) =>
        configuration.GetSection("Serilog:WriteTo").GetChildren().Any();

    /// <summary>
    /// Console and rolling file, applied only when the application declared no
    /// sinks of its own.
    /// </summary>
    /// <remarks>
    /// All-or-nothing rather than additive: an application that names its own
    /// destinations has said where its logs belong, and silently also writing
    /// them to the container disk would put the same records somewhere it did
    /// not ask for. Both defaults are local — stdout and a local file pin no
    /// vendor and reach no network.
    /// </remarks>
    private static void ApplyDefaultSinks(
        LoggerConfiguration loggerConfig,
        IHostEnvironment environment,
        string serviceName)
    {
        if (environment.IsDevelopment())
        {
            loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}" +
                               "{NewLine}{Exception}",
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code);
        }
        else
        {
            loggerConfig.WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());
        }

        var logPath = environment.IsDevelopment()
            ? $"logs/{serviceName}-.log"
            : $"/app/logs/{serviceName}-.log";

        loggerConfig.WriteTo.File(
            path: logPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            fileSizeLimitBytes: 10 * 1024 * 1024,
            rollOnFileSizeLimit: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] " +
                           "{SourceContext}: {Message:lj}{NewLine}{Exception}" +
                           "{NewLine}    CorrelationId: {CorrelationId}" +
                           "{NewLine}    ServiceName: {ServiceName}{NewLine}");
    }
}