using Microsoft.OpenApi.Models;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace Infrastructure.Extensions;

/// <summary>
/// Swagger/OpenAPI configuration extensions
/// </summary>
public static class SwaggerExtensions
{
    /// <summary>
    /// Adds Swagger documentation for the API
    /// </summary>
    public static IServiceCollection AddSwaggerDocumentation(
        this IServiceCollection services,
        string serviceName,
        string serviceVersion = "v1")
    {
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(serviceVersion, new OpenApiInfo
            {
                Title = $"SkillSwap {serviceName} API",
                Version = serviceVersion,
                Description = $"API for {serviceName} operations in the SkillSwap platform",
                Contact = new OpenApiContact
                {
                    Name = "SkillSwap Team",
                    Email = "api@skillswap.com"
                }
            });

            // Include XML comments for better documentation
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                c.IncludeXmlComments(xmlPath);
            }

            // Add JWT Bearer authentication
            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Use contract models for documentation
            c.EnableAnnotations();
            c.SupportNonNullableReferenceTypes();
        });

        return services;
    }

    /// <summary>
    /// Configures Swagger UI middleware
    /// </summary>
    public static IApplicationBuilder UseSwaggerDocumentation(
        this IApplicationBuilder app,
        string serviceName,
        string serviceVersion = "v1")
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint($"/swagger/{serviceVersion}/swagger.json", $"SkillSwap {serviceName} API {serviceVersion}");
            c.RoutePrefix = "api-docs";
            c.DocumentTitle = $"SkillSwap {serviceName} API Documentation";
            
            // Enable deep linking
            c.EnableDeepLinking();
            
            // Enable request duration
            c.DisplayRequestDuration();
            
            // Show only the service's operations by default
            c.DefaultModelsExpandDepth(2);
            c.DefaultModelExpandDepth(2);
        });

        return app;
    }
}