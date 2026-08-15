using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Girder.Infrastructure.Messaging;
using System.Reflection;

namespace Girder.Infrastructure.Extensions;

/// <summary>
/// Extension methods for messaging configuration (MassTransit + RabbitMQ)
/// </summary>
public static class MessagingExtensions
{
    /// <summary>
    /// Adds MassTransit with RabbitMQ configuration.
    /// </summary>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] consumerAssemblies)
    {
        return services.AddMessaging(configuration, consumerAssemblies, configureBus: null);
    }

    /// <summary>
    /// Adds MassTransit with RabbitMQ configuration.
    /// Use the optional <paramref name="configureBus"/> callback to add per-service bus
    /// configuration such as the EntityFramework Outbox. Each service that needs transactional
    /// outbox should configure it against its own DbContext (see PaymentService and
    /// ReferralService for pilot implementations).
    /// </summary>
    public static IServiceCollection AddMessaging(
        this IServiceCollection services,
        IConfiguration configuration,
        Assembly[] consumerAssemblies,
        Action<IBusRegistrationConfigurator>? configureBus = null)
    {
        var rabbitMqSettings = GetRabbitMqSettings(configuration);

        // Log RabbitMQ connection details at startup
        Console.WriteLine($"[MassTransit] Connecting to RabbitMQ at {rabbitMqSettings.Host}:{rabbitMqSettings.Port}");

        services.AddMassTransit(busConfig =>
        {
            // Add consumers from specified assemblies and log them
            foreach (var assembly in consumerAssemblies)
            {
                var consumerTypes = assembly.GetTypes()
                    .Where(t => t.GetInterfaces().Any(i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>)))
                    .ToList();

                Console.WriteLine($"[MassTransit] Found {consumerTypes.Count} consumers in {assembly.GetName().Name}:");
                foreach (var consumerType in consumerTypes)
                {
                    var messageTypes = consumerType.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IConsumer<>))
                        .Select(i => i.GetGenericArguments()[0].Name);
                    Console.WriteLine($"  - {consumerType.Name} (consumes: {string.Join(", ", messageTypes)})");
                }

                busConfig.AddConsumers(assembly);
            }

            // Allow per-service bus configuration (e.g., EntityFramework Outbox)
            configureBus?.Invoke(busConfig);

            // Configure RabbitMQ
            busConfig.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(rabbitMqSettings.Host, (ushort)rabbitMqSettings.Port, rabbitMqSettings.VirtualHost, h =>
                {
                    h.Username(rabbitMqSettings.Username);
                    h.Password(rabbitMqSettings.Password);
                });

                // Propagate correlation IDs across service boundaries via message headers
                cfg.UsePublishFilter(typeof(CorrelationIdPublishFilter<>), context);
                cfg.UseConsumeFilter(typeof(CorrelationIdConsumeFilter<>), context);

                // Configure retry policy
                cfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)));
                
                // Per-service EntityFramework Outbox is configured via the configureBus
                // callback (see AddEntityFrameworkOutbox in PaymentService/ReferralService).
                // This replaces the deprecated UseInMemoryOutbox and ensures messages are
                // written to the same DB transaction as business data.

                // Configure endpoints
                cfg.ConfigureEndpoints(context);
                
                // Set prefetch count for better performance
                cfg.PrefetchCount = 16;
                
                // Configure timeout
                cfg.UseTimeout(x => x.Timeout = TimeSpan.FromSeconds(30));
            });
            
            // Configure health checks
            busConfig.ConfigureHealthCheckOptions(opt =>
            {
                opt.Name = "masstransit";
                opt.Tags.Add("ready");
                opt.MinimalFailureStatus = HealthStatus.Unhealthy;
            });
        });
        
        // Note: RabbitMQ health check removed due to version conflicts
        // MassTransit already provides its own health checks
        
        return services;
    }
    
    /// <summary>
    /// Adds event bus abstraction
    /// </summary>
    public static IServiceCollection AddEventBus(this IServiceCollection services)
    {
        services.AddScoped<IEventBus, MassTransitEventBus>();
        return services;
    }
    
    /// <summary>
    /// Gets RabbitMQ settings from configuration
    /// </summary>
    private static RabbitMqSettings GetRabbitMqSettings(IConfiguration configuration)
    {
        var settings = new RabbitMqSettings();
        
        // Try environment variables first
        settings.Host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") 
            ?? configuration["RabbitMQ:Host"] 
            ?? "rabbitmq";
            
        settings.Username = Environment.GetEnvironmentVariable("RABBITMQ_USERNAME")
            ?? configuration["RabbitMQ:Username"]
            ?? "guest";
            
        settings.Password = Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")
            ?? configuration["RabbitMQ:Password"]
            ?? "guest";
            
        settings.VirtualHost = Environment.GetEnvironmentVariable("RABBITMQ_VHOST")
            ?? configuration["RabbitMQ:VirtualHost"]
            ?? "/";
            
        settings.Port = int.TryParse(
            Environment.GetEnvironmentVariable("RABBITMQ_PORT") ?? configuration["RabbitMQ:Port"],
            out var port) ? port : 5672;
        
        // Build connection string
        settings.ConnectionString = Environment.GetEnvironmentVariable("RABBITMQ_CONNECTION")
            ?? configuration.GetConnectionString("RabbitMQ")
            ?? $"amqp://{settings.Username}:{settings.Password}@{settings.Host}:{settings.Port}{settings.VirtualHost}";
        
        return settings;
    }
}

/// <summary>
/// RabbitMQ configuration settings
/// </summary>
public class RabbitMqSettings
{
    public string Host { get; set; } = "rabbitmq";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ConnectionString { get; set; } = string.Empty;
}

/// <summary>
/// Event bus interface for abstraction
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) 
        where TEvent : class;
}

/// <summary>
/// MassTransit implementation of event bus
/// </summary>
public class MassTransitEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitEventBus> _logger;

    public MassTransitEventBus(IPublishEndpoint publishEndpoint, ILogger<MassTransitEventBus> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var eventTypeName = typeof(TEvent).Name;
        _logger.LogInformation("[EventBus] Publishing event: {EventType}", eventTypeName);

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);
            _logger.LogInformation("[EventBus] Successfully published event: {EventType}", eventTypeName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EventBus] Failed to publish event: {EventType}", eventTypeName);
            throw;
        }
    }
}