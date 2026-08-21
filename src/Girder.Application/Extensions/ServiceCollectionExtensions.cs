using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Girder.Abstractions.Caching;
using Girder.Application.Abstractions;
using Girder.Application.Behaviors;
using Girder.Application.Hosting;
using Girder.Application.Interfaces;
using System.Reflection;
using Girder.Core.Logging;
using Microsoft.AspNetCore.Http;

namespace Girder.Application.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the mediator and Girder's pipeline behaviours.
    /// </summary>
    /// <remarks>
    /// The two cache behaviours are registered only where the service actually
    /// caches — that is, where one of <paramref name="assemblies"/> holds a type
    /// implementing <see cref="ICacheableQuery"/> or
    /// <see cref="ICacheInvalidatingCommand"/>. A service that caches nothing
    /// gets a shorter pipeline and needs no cache, so its composition root is
    /// not made to claim a decision it never took.
    /// <para>
    /// Where the service does cache, the cache is required rather than
    /// supplied: which cache runs is the operator's call. The requirement is
    /// checked at startup and names the type that triggered it.
    /// </para>
    /// </remarks>
    /// <param name="services">The collection to register into.</param>
    /// <param name="assemblies">The assemblies holding requests, handlers and validators.</param>
    public static IServiceCollection AddCQRS(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        // Add HttpContextAccessor for correlation ID tracking
        services.AddHttpContextAccessor();

        // Add Log Sanitizer
        services.AddSingleton<ILogSanitizer, LogSanitizer>();

        // What the caller actually caches decides whether the cache behaviours
        // are in the pipeline at all.
        var witness = FirstCachingType(assemblies);

        if (witness is not null)
        {
            services.RequiresProvider<IDistributedCacheService>(
                witness, "AddRedisCache(prefix) or AddInMemoryCache(prefix)");

            // CacheInvalidationBehavior clears ETags alongside the cache, so the
            // generator is not optional once the behaviour is in the pipeline.
            services.RequiresProvider<IETagGenerator>(
                witness, "AddHttpResponseCaching(configuration)");
        }

        // Add MediatR with all behaviors including cache invalidation
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblies(assemblies);

            // Add pipeline behaviors in order
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

            if (witness is not null)
            {
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>)); // Caching BEFORE Performance measurement
                cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>)); // Invalidation AFTER execution
            }

            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
        });

        // Add FluentValidation
        services.AddValidatorsFromAssemblies(assemblies);

        return services;
    }

    /// <summary>
    /// Names the requirement after the type that caused it, so the message
    /// points at a line the reader wrote rather than at an interface they have
    /// to go looking for.
    /// </summary>
    private static string? FirstCachingType(Assembly[] assemblies)
    {
        var cacheable = FirstImplementer(assemblies, typeof(ICacheableQuery));
        if (cacheable is not null)
        {
            return $"AddCQRS() ({cacheable.Name} implements {nameof(ICacheableQuery)})";
        }

        var invalidating = FirstImplementer(assemblies, typeof(ICacheInvalidatingCommand));
        if (invalidating is not null)
        {
            return $"AddCQRS() ({invalidating.Name} implements {nameof(ICacheInvalidatingCommand)})";
        }

        return null;
    }

    /// <summary>
    /// The first type in <paramref name="assemblies"/> that implements
    /// <paramref name="contract"/>, by name, so the message reads the same on
    /// every run.
    /// </summary>
    private static Type? FirstImplementer(Assembly[] assemblies, Type contract) =>
        assemblies.Where(a => a is not null)
                  .SelectMany(LoadableTypes)
                  .Where(t => t is { IsInterface: false, IsAbstract: false } && contract.IsAssignableFrom(t))
                  .OrderBy(t => t.FullName, StringComparer.Ordinal)
                  .FirstOrDefault();

    /// <summary>
    /// The types an assembly can produce. One unloadable type — a handler whose
    /// optional dependency is not deployed — must not take down a scan whose
    /// answer the rest of the assembly already gives.
    /// </summary>
    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
