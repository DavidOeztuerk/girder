using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace Girder.Infrastructure.Extensions;

/// <summary>
/// Extension methods for database configuration
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> with the resolved connection
    /// string, the readiness check and the development-time diagnostics.
    /// </summary>
    /// <param name="configureProvider">
    /// Binds the database provider, receiving the resolved connection string.
    /// The application supplies this because the provider package is the
    /// application's dependency — Girder never references one (ADR-0001):
    /// <code>
    /// services.AddDatabaseContext&lt;AppDbContext&gt;(config, "identity",
    ///     (options, cs) => options.UseNpgsql(cs, o => o.CommandTimeout(30)));
    /// </code>
    /// </param>
    /// <remarks>
    /// Do not enable EF Core's retry-on-failure here by default: a retrying
    /// execution strategy refuses user-initiated transactions, which is what
    /// message consumers open.
    /// </remarks>
    public static IServiceCollection AddDatabaseContext<TContext>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        Action<DbContextOptionsBuilder, string> configureProvider) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(configureProvider);

        var connectionString = GetConnectionString(configuration, serviceName);

        services.AddDbContext<TContext>(options =>
        {
            configureProvider(options, connectionString);

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
    /// Resolves the connection string: <c>ConnectionStrings__{serviceName}</c>
    /// from the environment, then the named connection string, then
    /// <c>DefaultConnection</c>.
    /// </summary>
    /// <remarks>
    /// This used to assemble a string from <c>POSTGRES_HOST</c>,
    /// <c>POSTGRES_DB</c> and friends when nothing was configured. That is
    /// Npgsql's key syntax, so the fallback silently decided the database
    /// engine — and it produced a plausible connection string for a server
    /// nobody had named. Missing configuration now fails instead.
    /// </remarks>
    private static string GetConnectionString(IConfiguration configuration, string serviceName)
    {
        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{serviceName}");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration.GetConnectionString(serviceName)
                ?? configuration.GetConnectionString("DefaultConnection");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No connection string for '{serviceName}'. Set the environment variable "
                + $"ConnectionStrings__{serviceName}, or configure ConnectionStrings:{serviceName} "
                + "or ConnectionStrings:DefaultConnection.");
        }

        return connectionString;
    }
}