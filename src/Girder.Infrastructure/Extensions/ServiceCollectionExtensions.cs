using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Girder.Infrastructure.Logging;
using Girder.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using StackExchange.Redis;
using System.Reflection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Resilience;
using Girder.Infrastructure.Security.Encryption;
using Girder.Infrastructure.Security.InputSanitization;
using Girder.Infrastructure.HealthChecks;
using Girder.Infrastructure.Caching;
using Girder.Infrastructure.Communication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Girder.Infrastructure.Models;
using Microsoft.AspNetCore.Http;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Girder.Core.Exceptions;
using Girder.Infrastructure.Security.Monitoring;
using Girder.Infrastructure.BackgroundServices;
using Girder.Infrastructure.Caching.Http;
using Girder.Infrastructure.Builder;
using Girder.Infrastructure.Builder.Modules;

namespace Girder.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
  /// <summary>
  /// Adds all infrastructure services and middleware to the DI container
  /// </summary>
  public static IServiceCollection AddSharedInfrastructure(
      this IServiceCollection services,
      IConfiguration configuration,
      IHostEnvironment environment,
      string serviceName)
  {
    // ═══════════════════════════════════════════════════════════════
    // NON-MODULAR SETUP (not covered by builder modules)
    // ═══════════════════════════════════════════════════════════════

    LogConfigurationSources(configuration, environment, serviceName);

    // Core Security Services (service registrations, NOT the auth scheme)
    services.AddScoped<IJwtService, JwtService>();
    services.AddSingleton<ITotpService, TotpService>();

    // Error Handling Services
    services.AddSingleton<IErrorMessageService, ErrorMessageService>();

    // Token Revocation — Redis-backed or In-Memory fallback
    var redisFromConfig = configuration.GetConnectionString("Redis");
    var redisFromConfigAlt = configuration["Redis:ConnectionString"];
    var redisFromEnv = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING");
    var redisConnectionString = !string.IsNullOrEmpty(redisFromConfig) ? redisFromConfig
        : !string.IsNullOrEmpty(redisFromConfigAlt) ? redisFromConfigAlt
        : redisFromEnv;

    if (!string.IsNullOrEmpty(redisConnectionString))
    {
      services.AddSingleton<ITokenRevocationService>(provider =>
      {
        var multiplexer = provider.GetService<IConnectionMultiplexer>();
        if (multiplexer == null)
        {
          multiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
        }
        var logger = provider.GetRequiredService<ILogger<RedisTokenRevocationService>>();
        return new RedisTokenRevocationService(multiplexer, logger);
      });
    }
    else
    {
      services.AddSingleton<ITokenRevocationService, InMemoryTokenRevocationService>();
    }

    // Configure Serilog
    LoggingConfiguration.ConfigureSerilog(configuration, environment, serviceName);
    services.AddSerilog();

    // Swagger Documentation
    services.AddEndpointsApiExplorer();
    services.AddSwaggerDocumentation(serviceName);

    // ═══════════════════════════════════════════════════════════════
    // MODULAR SETUP (delegated to builder modules)
    // NOTE: Role-based authorization (AddGirderAuthorization) and
    // Compliance are activated separately by services that need them.
    // Resource-based authorization is included here because its handlers
    // and policies are dormant unless endpoints explicitly use resource
    // policies (ResourceRead, ResourceOwner, etc.).
    // ═══════════════════════════════════════════════════════════════

    services.AddSharedInfrastructure(configuration, environment, serviceName, infra =>
    {
      infra.AddSecurityMonitoring();
      infra.AddResilience();
      infra.AddSecretManagement();
      infra.AddAuditLogging();
      infra.AddEncryption();
      infra.AddInputSanitization();
      infra.AddDistributedRateLimiting();
      infra.AddHealthChecks();
      infra.AddCommunication();
      infra.AddCaching();
      infra.AddObservability();
      infra.AddSecurityHeaders();
      infra.AddResourceAuthorization();
    });

    // ═══════════════════════════════════════════════════════════════
    // REMAINING NON-MODULAR SETUP
    // ═══════════════════════════════════════════════════════════════

    // JSON serialization — camelCase for APIs
    services.ConfigureHttpJsonOptions(options =>
    {
      options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
      options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
      options.SerializerOptions.WriteIndented = environment.IsDevelopment();
    });
    services.Configure<JsonOptions>(options =>
    {
      options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
      options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
      options.SerializerOptions.WriteIndented = environment.IsDevelopment();
    });

    // HTTP context accessor for correlation ID
    services.AddHttpContextAccessor();

    // CORS with secure defaults
    services.AddCors(options =>
    {
      options.AddDefaultPolicy(policy =>
          {
            var allowedOrigins = ResolveAllowedOrigins(configuration, environment);

            policy.WithOrigins(allowedOrigins)
                      .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
                      .WithHeaders(
                          "Content-Type",
                          "Authorization",
                          "X-Request-ID",
                          "X-Correlation-ID",
                          "X-Requested-With",
                          "X-SignalR-User-Agent",
                          "Accept",
                          "Accept-Language",
                          "Cache-Control",
                          "Pragma",
                          "baggage",
                          "sentry-trace"
                      )
                      .WithExposedHeaders(
                          "X-Request-ID",
                          "X-Correlation-ID",
                          "X-Pagination",
                          "Content-Disposition",
                          "baggage",
                          "sentry-trace"
                      )
                      .AllowCredentials()
                      .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
          });
    });

    return services;
  }

  /// <summary>
  /// Builder-based entry point — configure infrastructure modules selectively.
  /// </summary>
  public static IServiceCollection AddSharedInfrastructure(
      this IServiceCollection services,
      IConfiguration configuration,
      IHostEnvironment environment,
      string serviceName,
      Action<InfrastructureBuilder> configure)
  {
    var builder = new InfrastructureBuilder(services, configuration, environment, serviceName);
    configure(builder);
    return services;
  }

  /// <summary>
  /// Configures the middleware pipeline with all infrastructure components
  /// </summary>
  public static IApplicationBuilder UseSharedInfrastructure(
      this IApplicationBuilder app,
      IHostEnvironment environment,
      string serviceName)
  {
    return app.UseSharedInfrastructure(environment, serviceName, mw =>
    {
      mw.UseSecurityHeaders()
        .UseCorrelationId()
        .UseRequestLogging()
        .UseTelemetry()
        .UseExceptionHandling()
        .UseInputSanitization()
        .UseSerilogLogging()
        .UseCors()
        .UseSwagger()
        .UseRateLimiting()
        .UseHealthCheckEndpoints()
        .UseAuth()
        .UseSecurityAudit()
        .UsePermissions()
        .UseHttpCaching();
    });
  }

  /// <summary>
  /// Builder-based middleware pipeline — configure middleware selectively.
  /// </summary>
  public static IApplicationBuilder UseSharedInfrastructure(
      this IApplicationBuilder app,
      IHostEnvironment environment,
      string serviceName,
      Action<InfrastructureMiddlewareBuilder> configure)
  {
    var builder = new InfrastructureMiddlewareBuilder(app, environment, serviceName);
    configure(builder);
    return app;
  }

  /// <summary>
  /// Adds CQRS with Redis caching support and proper cache invalidation
  /// </summary>
  public static IServiceCollection AddCaching(
      this IServiceCollection services,
      string redisConnectionString)
  {
    // für Rate limiting 
    services.AddMemoryCache();

    // Configure Redis if connection string is provided
    IConnectionMultiplexer? connectionMultiplexer = null;

    if (!string.IsNullOrWhiteSpace(redisConnectionString))
    {
      try
      {
        // Configure Redis with retry logic
        var configOptions = ConfigurationOptions.Parse(redisConnectionString);
        configOptions.ConnectTimeout = 5000;
        configOptions.SyncTimeout = 5000;
        configOptions.AsyncTimeout = 5000;
        configOptions.ConnectRetry = 3;
        configOptions.AbortOnConnectFail = false;
        configOptions.KeepAlive = 60;

        // Enable admin commands only in Development (FLUSHALL, CONFIG, DEBUG etc.)
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        configOptions.AllowAdmin = string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);

        connectionMultiplexer = ConnectionMultiplexer.Connect(configOptions);

        // Register ConnectionMultiplexer as singleton for all services to use
        services.AddSingleton<IConnectionMultiplexer>(connectionMultiplexer);
        services.AddSingleton(connectionMultiplexer);

        // Add Redis cache
        services.AddStackExchangeRedisCache(options =>
        {
          options.ConnectionMultiplexerFactory = () => Task.FromResult(connectionMultiplexer);
          options.InstanceName = GetCurrentServiceName() + ":";
        });

        // Add Redis health check
        services.AddHealthChecks()
            .AddRedis(redisConnectionString,
                name: "redis",
                tags: new[] { "ready", "cache" },
                timeout: TimeSpan.FromSeconds(2));

      }
      catch
      {
        // Redis failed, fallback to memory cache
        services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
      }
    }
    else
    {
      // No Redis configured, use memory cache
      services.AddSingleton<IDistributedCache, MemoryDistributedCache>();
    }

    return services;
  }

  /// <summary>
  /// Adds JWT Authentication with complete configuration
  ///
  /// This is the MASTER JWT authentication setup used by all services.
  /// Features:
  /// - Zero ClockSkew for strict token expiration
  /// - Environment variable support (JWT_SECRET, JWT_ISSUER, JWT_AUDIENCE)
  /// - Token revocation check via JwtBearerEvents.OnTokenValidated
  /// - Custom 401 JSON responses
  /// - Placeholder secret validation for production safety
  /// </summary>
  public static IServiceCollection AddJwtAuthentication(
      this IServiceCollection services,
      IConfiguration configuration,
      IHostEnvironment environment)
  {
    // Load secret from Environment Variable FIRST, then appsettings
    var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? configuration["JwtSettings:Secret"];

    // Log which secret source was used (safe: source name and length only)
    var logger = services.BuildServiceProvider().GetService<ILogger<Microsoft.Extensions.DependencyInjection.ServiceCollection>>();
    var secretSource = Environment.GetEnvironmentVariable("JWT_SECRET") != null ? "Environment Variable" : "appsettings.json";
    logger?.LogInformation("JWT_SECRET loaded from: {Source}, Length: {Length}",
        secretSource, secret?.Length ?? 0);

    // Validate JWT secret is properly configured
    if (string.IsNullOrWhiteSpace(secret) || secret.Contains("REPLACE_WITH_SECURE_SECRET_IN_PRODUCTION"))
    {
      if (environment.IsProduction())
      {
        throw new ConfigurationException("JWT_SECRET", "JwtSettings",
            "JWT Secret not configured or using placeholder value. " +
            "Set JWT_SECRET environment variable to the SAME value for ALL services. " +
            "Generate a secure secret with: openssl rand -base64 32");
      }

      throw new ConfigurationException("JWT_SECRET", "JwtSettings",
          "JWT Secret not configured. Set JWT_SECRET environment variable to the SAME value for ALL services.");
    }

    var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER")
        ?? configuration["JwtSettings:Issuer"]
        ?? throw new ConfigurationException("JWT_ISSUER", "JwtSettings", "JWT Issuer not configured. Please set JWT_ISSUER environment variable or configure JwtSettings:Issuer");

    var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
        ?? configuration["JwtSettings:Audience"]
        ?? throw new ConfigurationException("JWT_AUDIENCE", "JwtSettings", "JWT Audience not configured. Please set JWT_AUDIENCE environment variable or configure JwtSettings:Audience");

    var expireMinutes = int.TryParse(
        Environment.GetEnvironmentVariable("JwtSettings__ExpireMinutes") ?? configuration["JwtSettings:ExpireMinutes"],
        out var expire) ? expire : 60;

    // CRITICAL: Configure JwtSettings with Environment Variables!
    // This ensures JwtService uses the SAME secret as the authentication middleware
    services.Configure<JwtSettings>(opts =>
    {
      opts.Secret = secret;
      opts.Issuer = issuer;
      opts.Audience = audience;
      opts.ExpireMinutes = expireMinutes;
    });

    // Create signing key
    var secretBytes = Encoding.UTF8.GetBytes(secret);
    var signingKey = new SymmetricSecurityKey(secretBytes)
    {
      KeyId = "GirderKey" // Must match the KeyId in JwtService
    };

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
          // In development, allow HTTP for local testing
          // In production/staging, require HTTPS for security
          opts.RequireHttpsMetadata = !environment.IsDevelopment();
          opts.SaveToken = true;
          opts.MapInboundClaims = false;

          opts.TokenValidationParameters = new TokenValidationParameters
          {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.Zero,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
          };

          opts.Events = new JwtBearerEvents
          {
            // CRITICAL: Allow access_token in query string for WebSocket/SignalR connections
            // WebSockets cannot send Authorization headers, so token must be in query string
            OnMessageReceived = context =>
                {
                  var accessToken = context.Request.Query["access_token"].FirstOrDefault();
                  var path = context.HttpContext.Request.Path;

                  // Only apply to SignalR hub endpoints (both direct and gateway-proxied paths)
                  if (!string.IsNullOrEmpty(accessToken) &&
                          (path.StartsWithSegments("/api/session/hub") ||
                           path.StartsWithSegments("/api/session/sfu") ||
                           path.StartsWithSegments("/hubs") ||
                           path.Value?.Contains("/hubs/", StringComparison.OrdinalIgnoreCase) == true))
                  {
                    context.Token = accessToken;
                  }
                  return Task.CompletedTask;
                },
            OnTokenValidated = async context =>
                {
                  var tokenRevocationService = context.HttpContext.RequestServices
                          .GetRequiredService<ITokenRevocationService>();

                  var jti = context.Principal?.FindFirst("jti")?.Value;
                  if (!string.IsNullOrEmpty(jti))
                  {
                    var isRevoked = await tokenRevocationService.IsTokenRevokedAsync(jti);
                    if (isRevoked)
                    {
                      context.Fail("Token has been revoked");
                    }
                  }
                },
            OnAuthenticationFailed = context =>
                {
                  if (context.Exception is SecurityTokenExpiredException)
                    context.Response.Headers.Append("Token-Expired", "true");
                  return Task.CompletedTask;
                },
            OnChallenge = async context =>
                {
                  context.HandleResponse();
                  context.Response.StatusCode = 401;
                  context.Response.ContentType = "application/json";
                  var result = System.Text.Json.JsonSerializer.Serialize(new
                  {
                    error = "unauthorized",
                    message = "You are not authorized to access this resource"
                  });
                  await context.Response.WriteAsync(result);
                }
          };
        });

    return services;
  }

  /// <summary>
  /// Configures health check endpoints with proper response formatting
  /// </summary>
  private static void ConfigureHealthCheckEndpoints(IApplicationBuilder app)
  {
    var healthCheckOptions = new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
      ResponseWriter = async (context, report) =>
      {
        context.Response.ContentType = "application/json";
        var response = new
        {
          status = report.Status.ToString(),
          timestamp = DateTime.UtcNow,
          durationMs = report.TotalDuration.TotalMilliseconds,
          checks = report.Entries.Select(e => new
          {
            name = e.Key,
            status = e.Value.Status.ToString(),
            durationMs = e.Value.Duration.TotalMilliseconds,
            tags = e.Value.Tags,
            error = e.Value.Exception?.Message
          })
        };
        await context.Response.WriteAsync(
                  System.Text.Json.JsonSerializer.Serialize(response,
                  new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
      }
    };

    // General health endpoint
    app.UseHealthChecks("/health", healthCheckOptions);

    // Liveness probe - only checks if the service is alive
    app.UseHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
      Predicate = r => r.Tags.Contains("live"),
      ResponseWriter = healthCheckOptions.ResponseWriter,
      ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status200OK
            }
    });

    // Readiness probe - checks if the service is ready to handle requests
    app.UseHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
      Predicate = r => r.Tags.Contains("ready"),
      ResponseWriter = healthCheckOptions.ResponseWriter,
      ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
    });
  }

  /// <summary>
  /// Gets the current service name from assembly
  /// </summary>
  private static string GetCurrentServiceName()
  {
    var assembly = Assembly.GetEntryAssembly();
    var assemblyName = assembly?.GetName().Name ?? "UnknownService";

    if (assemblyName.Contains("UserService")) return "userservice";
    if (assemblyName.Contains("JobService")) return "jobservice";
    if (assemblyName.Contains("NotificationService")) return "notificationservice";
    if (assemblyName.Contains("ReferralService")) return "referralservice";
    if (assemblyName.Contains("BookingService")) return "bookingservice";
    if (assemblyName.Contains("SessionService")) return "sessionservice";
    if (assemblyName.Contains("PaymentService")) return "paymentservice";

    return assemblyName;
  }

  private static string[] ResolveAllowedOrigins(IConfiguration configuration, IHostEnvironment environment)
  {
    var configuredOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
    var envOrigins = ParseOrigins(configuration["CORS_ORIGINS"] ?? Environment.GetEnvironmentVariable("CORS_ORIGINS"));
    var frontendOrigins = ParseOrigins(configuration["FRONTEND_URL"] ?? Environment.GetEnvironmentVariable("FRONTEND_URL"));

    var origins = configuredOrigins
        .Concat(envOrigins)
        .Concat(frontendOrigins)
        .Select(NormalizeOrigin)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Select(origin => origin!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (origins.Count == 0 && environment.IsDevelopment())
    {
      origins.Add("http://localhost:3000");
    }

    return origins.ToArray();
  }

  private static IEnumerable<string> ParseOrigins(string? delimitedOrigins)
  {
    if (string.IsNullOrWhiteSpace(delimitedOrigins))
    {
      return Array.Empty<string>();
    }

    return delimitedOrigins
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(origin => origin.Trim());
  }

  private static string? NormalizeOrigin(string? origin)
  {
    if (string.IsNullOrWhiteSpace(origin))
    {
      return null;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
      return null;
    }

    return uri.GetLeftPart(UriPartial.Authority);
  }

  /// <summary>
  /// Logs which configuration sources are loaded and which critical secrets are present.
  /// Shows only source and length — no secret material is logged.
  /// </summary>
  private static void LogConfigurationSources(
      IConfiguration configuration,
      IHostEnvironment environment,
      string serviceName)
  {
    var separator = new string('=', 60);
    Console.WriteLine(separator);
    Console.WriteLine($"  CONFIG DIAGNOSTICS - {serviceName}");
    Console.WriteLine($"  Environment: {environment.EnvironmentName}");
    Console.WriteLine(separator);

    // Check critical secrets: name → (dockerEnvVar, configKey)
    // dockerEnvVar = the env var name as set by docker-compose environment: block
    // configKey = the .NET configuration key (reads from appsettings.json)
    var secrets = new Dictionary<string, (string dockerEnvVar, string configKey)>
    {
      ["JWT_SECRET"] = ("JwtSettings__Secret", "JwtSettings:Secret"),
      ["POSTGRES"] = ("ConnectionStrings__DefaultConnection", "ConnectionStrings:DefaultConnection"),
      ["RABBITMQ_PASSWORD"] = ("RabbitMQ__Password", "RabbitMQ:Password"),
      ["REDIS"] = ("ConnectionStrings__Redis", "ConnectionStrings:Redis"),
      ["M2M_SECRET"] = ("ServiceCommunication__M2M__ClientSecret", "ServiceCommunication:M2M:ClientSecret"),
      ["ENCRYPTION_KEY"] = ("SecretManager__EncryptionKey", "SecretManager:EncryptionKey"),
    };

    // Service-specific secrets
    if (serviceName.Contains("Notification", StringComparison.OrdinalIgnoreCase))
    {
      secrets["TWILIO_SID"] = ("Twilio__AccountSid", "Twilio:AccountSid");
      secrets["TWILIO_TOKEN"] = ("Twilio__AuthToken", "Twilio:AuthToken");
      secrets["SMTP_PASSWORD"] = ("Email__SmtpPassword", "Email:SmtpPassword");
      secrets["FIREBASE_KEY_ID"] = ("Firebase__PrivateKeyId", "Firebase:PrivateKeyId");
    }
    if (serviceName.Contains("Payment", StringComparison.OrdinalIgnoreCase))
    {
      secrets["STRIPE_SECRET"] = ("Stripe__SecretKey", "Stripe:SecretKey");
      secrets["STRIPE_WEBHOOK"] = ("Stripe__WebhookSecret", "Stripe:WebhookSecret");
    }
    if (serviceName.Contains("User", StringComparison.OrdinalIgnoreCase))
    {
      secrets["LINKEDIN_ID"] = ("OAuth__LinkedIn__ClientId", "OAuth:LinkedIn:ClientId");
      secrets["LINKEDIN_SECRET"] = ("OAuth__LinkedIn__ClientSecret", "OAuth:LinkedIn:ClientSecret");
    }

    foreach (var (label, (dockerEnvVar, configKey)) in secrets)
    {
      // Check if set via environment variable (docker-compose environment: block)
      var envValue = Environment.GetEnvironmentVariable(dockerEnvVar);
      // Check configuration (merges env vars + appsettings.json)
      var configValue = configuration[configKey];

      var value = configValue ?? envValue;
      // Determine source: if env var exists, it came from docker-compose (Infisical → shell → compose → container)
      var source = !string.IsNullOrEmpty(envValue) ? "ENV" : "CONFIG";

      if (value is null && envValue is null && configValue is null)
      {
        Console.WriteLine($"  {label,-20} : NOT SET");
      }
      else if (string.IsNullOrEmpty(value))
      {
        // Value is explicitly configured but empty (e.g. SMTP_PASSWORD for MailHog)
        Console.WriteLine($"  {label,-20} : SET (empty) [from {source}]");
      }
      else
      {
        Console.WriteLine($"  {label,-20} : SET ({value.Length} chars) [from {source}]");
      }
    }

    // Check if secrets came from Infisical (injected via docker-compose environment: block)
    // If JwtSettings__Secret exists as env var, it was set by docker-compose from ${JWT_SECRET}
    var jwtFromEnv = Environment.GetEnvironmentVariable("JwtSettings__Secret");
    var jwtFromConfig = configuration["JwtSettings:Secret"];
    var secretsFromEnvBlock = !string.IsNullOrEmpty(jwtFromEnv);
    Console.WriteLine($"  {"SOURCE",-20} : {(secretsFromEnvBlock ? "docker-compose environment: block (Infisical/shell)" : ".env.docker files + appsettings.json")}");

    Console.WriteLine(separator);
  }
}
