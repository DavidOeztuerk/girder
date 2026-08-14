using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
using Serilog.Sinks.Elasticsearch;
using Serilog.Exceptions;
using Core.Common.Exceptions;

namespace Infrastructure.Logging;

public static class LoggingConfiguration
{
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

        // Console output with different formatting for different environments
        if (environment.IsDevelopment())
        {
            // Development: Readable format with controlled exception output
            loggerConfig.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}" +
                               "{NewLine}{Exception}",
                theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code);
        }
        else
        {
            // Production: Structured JSON logging
            loggerConfig.WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());
        }

        // File logging with rotation
        var logPath = environment.IsDevelopment() 
            ? $"logs/{serviceName}-.log" 
            : $"/app/logs/{serviceName}-.log";

        loggerConfig.WriteTo.File(
            path: logPath,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7,
            fileSizeLimitBytes: 10 * 1024 * 1024, // 10MB
            rollOnFileSizeLimit: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] " +
                           "{SourceContext}: {Message:lj}{NewLine}{Exception}" +
                           "{NewLine}    CorrelationId: {CorrelationId}" +
                           "{NewLine}    ServiceName: {ServiceName}{NewLine}");

        // Different log levels for different environments
        if (environment.IsDevelopment())
        {
            loggerConfig.MinimumLevel.Debug();
        }
        else if (environment.IsProduction())
        {
            loggerConfig.MinimumLevel.Information();
        }

        // Add ELK Stack integration if configured
        var elasticUri = configuration.GetConnectionString("Elasticsearch");
        if (!string.IsNullOrEmpty(elasticUri))
        {
            loggerConfig.WriteTo.Elasticsearch(new ElasticsearchSinkOptions(new Uri(elasticUri))
            {
                IndexFormat = $"skillswap-{serviceName.ToLower()}-logs-{DateTime.UtcNow:yyyy-MM}",
                AutoRegisterTemplate = true,
                AutoRegisterTemplateVersion = AutoRegisterTemplateVersion.ESv7
            });
        }

        Log.Logger = loggerConfig.CreateLogger();
    }
}