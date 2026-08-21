using Girder.Abstractions.Caching;
using Girder.Application.Behaviors;
using Girder.Application.Extensions;
using Girder.Fixtures.CachingCqrs;
using Girder.Fixtures.PlainCqrs;
using Girder.InMemory.Caching;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Girder.Infrastructure.Tests.Cqrs;

/// <summary>
/// <c>AddCQRS()</c> takes a cache only where the service actually caches, and
/// says so at startup rather than on the first request.
/// </summary>
/// <remarks>
/// The pipeline used to register both cache behaviours unconditionally. Their
/// constructor parameters were nullable, so the code read as if a cache were
/// optional — but the container does not honour C# nullability, and the first
/// <c>Send</c> died on <c>IDistributedCacheService</c>, a type the caller never
/// wrote. A service that caches nothing had to register a cache anyway, which
/// left its composition root claiming a decision it had not made.
/// </remarks>
[Trait("Category", "Unit")]
public class CqrsCacheRequirementTests
{
    private static readonly Assembly CachesNothing = typeof(Ping).Assembly;
    private static readonly Assembly Caches = typeof(GetJobQuery).Assembly;

    private static ServiceCollection Bare()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    /// <summary>Runs the startup filters the way the host does.</summary>
    private static Action ConfigurePipeline(WebApplication app) => () =>
    {
        foreach (var filter in app.Services.GetServices<IStartupFilter>())
        {
            filter.Configure(_ => { })(app);
        }
    };

    private static WebApplication BuildApp(Assembly assembly, Action<IServiceCollection>? providers = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddCQRS(assembly);
        providers?.Invoke(builder.Services);
        return builder.Build();
    }

    private static IReadOnlyList<Type> Behaviors(IServiceCollection services) =>
        services.Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
                .Select(d => d.ImplementationType!)
                .ToArray();

    // ---- a service that caches nothing ----------------------------------

    [Fact]
    public async Task Der_Mediator_kommt_ohne_registrierten_Cache_hoch()
    {
        var services = Bare();
        services.AddCQRS(CachesNothing);

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();

        (await mediator.Send(new Ping("hallo"))).Should().Be("hallo");
    }

    [Fact]
    public void Ohne_zwischenspeichernde_Anfrage_stehen_die_Cache_Behaviors_nicht_in_der_Pipeline()
    {
        var services = Bare();
        services.AddCQRS(CachesNothing);

        Behaviors(services).Should().NotContain([
            typeof(CachingBehavior<,>),
            typeof(CacheInvalidationBehavior<,>)
        ]);
    }

    [Fact]
    public void Ohne_zwischenspeichernde_Anfrage_faellt_der_Start_nicht_ueber_einen_fehlenden_Cache()
    {
        var app = BuildApp(CachesNothing);

        ConfigurePipeline(app).Should().NotThrow();
    }

    // ---- a service that does cache --------------------------------------

    [Fact]
    public void Eine_zwischenspeichernde_Anfrage_ohne_Cache_scheitert_beim_Start()
    {
        var app = BuildApp(Caches);

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage("*IDistributedCacheService*");
    }

    [Fact]
    public void Die_Meldung_nennt_den_Typ_der_die_Anforderung_ausloest()
    {
        // Naming the interface alone sends the reader grepping. Naming the type
        // that implements it points at the line that has to change.
        var app = BuildApp(Caches);

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(GetJobQuery)}*{nameof(Girder.Application.Interfaces.ICacheableQuery)}*");
    }

    [Fact]
    public void Verlangt_wird_der_Cache_und_sonst_nichts()
    {
        // Clearing stale ETags is a write to this same cache. It used to be
        // asked for through IETagGenerator, so a service that wanted a cache
        // was made to register HTTP response caching as well.
        var app = BuildApp(Caches);

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("1 provider registration")
            .And.NotContain("IETagGenerator");
    }

    [Fact]
    public void Die_Meldung_nennt_den_Aufruf_der_sie_behebt()
    {
        var app = BuildApp(Caches);

        ConfigurePipeline(app).Should().Throw<InvalidOperationException>()
            .WithMessage("*AddInMemoryCache*");
    }

    [Fact]
    public void Ein_selbst_registrierter_Cache_erfuellt_die_Bedingung()
    {
        // The check asks for the interface, not for who registered it: a
        // provider the caller wrote themselves counts.
        var app = BuildApp(Caches, services =>
            services.AddSingleton(Substitute.For<IDistributedCacheService>()));

        ConfigurePipeline(app).Should().NotThrow();
    }

    [Fact]
    public void Mit_Cache_steht_die_zwischenspeichernde_Pipeline()
    {
        var services = Bare();
        services.AddCQRS(Caches);

        Behaviors(services).Should().Contain([
            typeof(CachingBehavior<,>),
            typeof(CacheInvalidationBehavior<,>)
        ]);
    }

    [Fact]
    public async Task Mit_Cache_wird_die_zweite_gleiche_Anfrage_nicht_mehr_behandelt()
    {
        // Registering the behaviour is not the point; caching is. Without this
        // the conditional registration could drop the behaviour and every
        // other test here would still pass.
        var services = Bare();
        services.AddInMemoryCache("cqrs-messung");
        services.AddCQRS(Caches);

        var mediator = services.BuildServiceProvider().GetRequiredService<IMediator>();
        var vorher = GetJobQueryHandler.Calls;

        await mediator.Send(new GetJobQuery("4711"));
        await mediator.Send(new GetJobQuery("4711"));

        (GetJobQueryHandler.Calls - vorher).Should().Be(1);
    }

    // ---- the order of the six is deliberate ------------------------------

    [Fact]
    public void Die_Reihenfolge_der_Behaviors_bleibt_wie_sie_ist()
    {
        // Caching sits before the performance measurement on purpose, and
        // invalidation after the handler has run. Conditional registration must
        // not reshuffle what stays.
        var services = Bare();
        services.AddCQRS(Caches);

        Behaviors(services).Should().Equal([
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(CacheInvalidationBehavior<,>),
            typeof(PerformanceBehavior<,>),
            typeof(AuditBehavior<,>)
        ]);
    }

    [Fact]
    public void Ohne_Cache_bleiben_die_uebrigen_vier_in_ihrer_Reihenfolge()
    {
        var services = Bare();
        services.AddCQRS(CachesNothing);

        Behaviors(services).Should().Equal([
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(PerformanceBehavior<,>),
            typeof(AuditBehavior<,>)
        ]);
    }
}
