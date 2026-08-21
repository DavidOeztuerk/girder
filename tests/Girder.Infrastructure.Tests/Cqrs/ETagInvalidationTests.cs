using Girder.Abstractions.Caching;
using Girder.Application.Extensions;
using Girder.Fixtures.CachingCqrs;
using Girder.InMemory.Caching;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Girder.Infrastructure.Tests.Cqrs;

/// <summary>
/// Clearing stale ETags is a cache write, not an HTTP concern.
/// </summary>
/// <remarks>
/// The behaviour used to take <c>IETagGenerator</c> — an interface whose
/// documentation says "for HTTP responses", whose patterns are API paths, and
/// which only <c>AddHttpResponseCaching()</c> ever registered. So a service had
/// to switch on HTTP response caching to make its CQRS pipeline start, and its
/// composition root then claimed something nobody meant. All the behaviour ever
/// called was one <c>RemoveByPatternAsync</c> on the cache it already held.
/// </remarks>
[Trait("Category", "Unit")]
public class ETagInvalidationTests
{
    private static readonly Assembly Caches = typeof(UpdateJobCommand).Assembly;

    /// <summary>A service with a cache and no HTTP response caching at all.</summary>
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryCache("etag-test");
        services.AddCQRS(Caches);
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Ein_Befehl_mit_ETag_Mustern_laeuft_ohne_HTTP_Antwort_Caching()
    {
        var mediator = Build().GetRequiredService<IMediator>();

        (await mediator.Send(new UpdateJobCommand("4711"))).Should().Be("updated 4711");
    }

    [Fact]
    public async Task Die_Invalidierung_entfernt_die_ETag_Schluessel()
    {
        // Against the cache, not against a mock: a mock would only show that a
        // method was called, and the question here is with which key.
        var provider = Build();
        var cache = provider.GetRequiredService<IDistributedCacheService>();

        await cache.SetAsync($"{CacheKeys.ETagPrefix}/api/jobs/4711", "\"passt\"");
        await cache.SetAsync($"{CacheKeys.ETagPrefix}/api/andere/1", "\"bleibt\"");

        await provider.GetRequiredService<IMediator>().Send(new UpdateJobCommand("4711"));

        (await cache.ExistsAsync($"{CacheKeys.ETagPrefix}/api/jobs/4711")).Should().BeFalse();
        (await cache.ExistsAsync($"{CacheKeys.ETagPrefix}/api/andere/1")).Should().BeTrue();
    }

    [Fact]
    public async Task Ohne_erklaerte_Muster_wird_kein_ETag_angefasst()
    {
        // The command says which paths it makes stale. Deriving them from cache
        // keys would mean guessing another application's routing.
        var provider = Build();
        var cache = provider.GetRequiredService<IDistributedCacheService>();

        await cache.SetAsync($"{CacheKeys.ETagPrefix}/api/jobs/4711", "\"bleibt\"");

        await provider.GetRequiredService<IMediator>().Send(new ArchiveJobCommand("4711"));

        (await cache.ExistsAsync($"{CacheKeys.ETagPrefix}/api/jobs/4711")).Should().BeTrue();
    }
}
