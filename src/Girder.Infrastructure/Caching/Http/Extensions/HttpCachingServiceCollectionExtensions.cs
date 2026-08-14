using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Caching.Http;

/// <summary>
/// Extension methods for configuring HTTP response caching services.
/// </summary>
public static class HttpCachingServiceCollectionExtensions
{
    /// <summary>
    /// Adds HTTP response caching services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration instance.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // In Program.cs
    /// builder.Services.AddHttpResponseCaching(builder.Configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddHttpResponseCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Bind configuration
        services.Configure<HttpCachingOptions>(
            configuration.GetSection(HttpCachingOptions.SectionName));

        // Register services
        services.AddSingleton<ICachePolicyProvider, CachePolicyProvider>();
        services.AddSingleton<IETagGenerator, ETagGenerator>();

        return services;
    }

    /// <summary>
    /// Adds HTTP response caching services with custom configuration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// builder.Services.AddHttpResponseCaching(options =>
    /// {
    ///     options.Enabled = true;
    ///     options.DefaultPrivateMaxAge = 300;
    ///     options.Policies.Add(new HttpCachePolicy
    ///     {
    ///         PathPattern = "/api/users/*",
    ///         MaxAge = 60,
    ///         Private = true
    ///     });
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddHttpResponseCaching(
        this IServiceCollection services,
        System.Action<HttpCachingOptions> configureOptions)
    {
        // Configure options
        services.Configure(configureOptions);

        // Register services
        services.AddSingleton<ICachePolicyProvider, CachePolicyProvider>();
        services.AddSingleton<IETagGenerator, ETagGenerator>();

        return services;
    }
}

/// <summary>
/// Extension methods for configuring HTTP response caching middleware.
/// </summary>
public static class HttpCachingApplicationBuilderExtensions
{
    /// <summary>
    /// Adds HTTP response caching headers middleware to the pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    /// <remarks>
    /// This middleware should be placed after UseAuthentication and UseAuthorization,
    /// but before UseEndpoints/MapControllers.
    /// </remarks>
    /// <example>
    /// <code>
    /// // In Program.cs
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseHttpResponseCachingHeaders(); // Add here
    /// app.MapControllers();
    /// </code>
    /// </example>
    public static IApplicationBuilder UseHttpResponseCachingHeaders(
        this IApplicationBuilder app)
    {
        return app.UseMiddleware<ResponseCachingHeaderMiddleware>();
    }
}
