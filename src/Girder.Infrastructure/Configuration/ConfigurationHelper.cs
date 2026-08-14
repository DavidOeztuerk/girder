using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;

namespace Infrastructure.Configuration;

public static class ConfigurationHelper
{
    public static IConfiguration BuildConfiguration(string[] args, string? basePath = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(basePath ?? Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", 
                optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args);
            
        return builder.Build();
    }
    
    public static string GetConnectionString(IConfiguration configuration, string name = "DefaultConnection")
    {
        var connectionString = configuration.GetConnectionString(name);
        
        if (string.IsNullOrEmpty(connectionString))
        {
            // Build from environment variables
            switch (name)
            {
                case "DefaultConnection":
                    connectionString = BuildPostgresConnectionString();
                    break;
                case "Redis":
                    connectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION") 
                        ?? throw new InvalidOperationException("Redis connection not configured");
                    break;
                case "RabbitMQ":
                    connectionString = BuildRabbitMQConnectionString();
                    break;
                default:
                    throw new InvalidOperationException($"Unknown connection string: {name}");
            }
        }
        
        return connectionString;
    }
    
    private static string BuildPostgresConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "skillswap";
        var username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "skillswap";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") 
            ?? throw new InvalidOperationException("POSTGRES_PASSWORD environment variable is required");
        
        return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
    
    private static string BuildRabbitMQConnectionString()
    {
        var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? "5672";
        var username = Environment.GetEnvironmentVariable("RABBITMQ_USER") ?? "guest";
        var password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD") ?? "guest";
        var vhost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST") ?? "/";
        
        return $"amqp://{username}:{password}@{host}:{port}{vhost}";
    }
    
    public static T GetRequiredConfiguration<T>(IConfiguration configuration, string section) where T : new()
    {
        var config = new T();
        configuration.GetSection(section).Bind(config);
        return config;
    }
}
