using Microsoft.OpenApi;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;

namespace Girder.Infrastructure.Extensions;

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
                Title = $"Girder {serviceName} API",
                Version = serviceVersion,
                Description = $"API for {serviceName} operations in the Girder platform",
                Contact = new OpenApiContact
                {
                    Name = "Girder Team",
                    Email = "api@girder.com"
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

            // Microsoft.OpenApi 2.x: AddSecurityRequirement nimmt jetzt ein
            // Func<OpenApiDocument, OpenApiSecurityRequirement>, und ein Schema
            // wird ueber OpenApiSecuritySchemeReference referenziert statt ueber
            // ein OpenApiSecurityScheme mit gesetztem Reference-Feld.
            c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
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
            c.SwaggerEndpoint($"/swagger/{serviceVersion}/swagger.json", $"Girder {serviceName} API {serviceVersion}");
            c.RoutePrefix = "api-docs";
            c.DocumentTitle = $"Girder {serviceName} API Documentation";
            
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