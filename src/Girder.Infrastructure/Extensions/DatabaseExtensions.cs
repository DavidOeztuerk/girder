using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace Infrastructure.Extensions;

/// <summary>
/// Extension methods for database configuration
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Adds a database context with PostgreSQL configuration
    /// </summary>
    public static IServiceCollection AddDatabaseContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string? migrationAssembly = null) where TContext : DbContext
    {
        var connectionString = GetConnectionString(configuration, serviceName);
        
        services.AddDbContext<TContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                // NOTE: EnableRetryOnFailure removed - conflicts with MassTransit consumers
                // and causes "NpgsqlRetryingExecutionStrategy does not support user-initiated transactions" errors
                // Resilience is handled at the application level via MassTransit retry policies

                // Set migration assembly if specified
                if (!string.IsNullOrEmpty(migrationAssembly))
                {
                    npgsql.MigrationsAssembly(migrationAssembly);
                }

                // Enable command timeout for long-running queries
                npgsql.CommandTimeout(30);

                // Use split queries for better performance with multiple includes
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
            
            // Enable detailed errors and sensitive data logging in development
            var environment = services.BuildServiceProvider()
                .GetRequiredService<IHostEnvironment>();
            
            if (environment.IsDevelopment())
            {
                options.EnableDetailedErrors();
                // Note: Sensitive data logging exposes parameter values in logs — only enable when actively debugging queries
                // options.EnableSensitiveDataLogging();
            }
        });
        
        // Add health check for the database
        services.AddHealthChecks()
            .AddDbContextCheck<TContext>(
                name: $"{serviceName}-database",
                tags: new[] { "ready", "db" });
        
        return services;
    }
    
    /// <summary>
    /// Configures database options for different environments
    /// </summary>
    public static IServiceCollection ConfigureDatabaseOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        return services;
    }
    
    /// <summary>
    /// Gets the connection string for a service
    /// </summary>
    private static string GetConnectionString(IConfiguration configuration, string serviceName)
    {
        // Try environment variable first
        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{serviceName}");
        
        // Try configuration
        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = configuration.GetConnectionString(serviceName)
                ?? configuration.GetConnectionString("DefaultConnection");
        }
        
        // Build from individual components as fallback
        if (string.IsNullOrEmpty(connectionString))
        {
            var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") 
                ?? configuration["Database:Host"]
                ?? $"postgres_{serviceName.ToLower()}";
                
            var database = Environment.GetEnvironmentVariable("POSTGRES_DB")
                ?? configuration["Database:Database"]
                ?? serviceName.ToLower();
                
            var user = Environment.GetEnvironmentVariable("POSTGRES_USER")
                ?? configuration["Database:Username"]
                ?? "skillswap";
                
            var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD")
                ?? configuration["Database:Password"]
                ?? throw new InvalidOperationException("Database password not configured");
                
            var port = Environment.GetEnvironmentVariable("POSTGRES_PORT")
                ?? configuration["Database:Port"]
                ?? "5432";
            
            connectionString = $"Host={host};Database={database};Username={user};Password={password};Port={port};Trust Server Certificate=true";
        }
        
        return connectionString;
    }
}

/// <summary>
/// Database configuration options
/// </summary>
public class DatabaseOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool EnableRetry { get; set; } = true;
    public int MaxRetryCount { get; set; } = 5;
    public int CommandTimeout { get; set; } = 30;
}