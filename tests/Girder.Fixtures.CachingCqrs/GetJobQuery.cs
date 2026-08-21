using Girder.Application.Interfaces;
using MediatR;

namespace Girder.Fixtures.CachingCqrs;

/// <summary>A query that asks to be cached.</summary>
public sealed record GetJobQuery(string JobId) : IRequest<string>, ICacheableQuery
{
    public string CacheKey => $"job:{JobId}";

    public TimeSpan CacheDuration => TimeSpan.FromMinutes(5);
}

/// <summary>Counts how often it actually ran, so a cache hit is visible.</summary>
public sealed class GetJobQueryHandler : IRequestHandler<GetJobQuery, string>
{
    /// <summary>How many times the handler was reached.</summary>
    public static int Calls;

    public Task<string> Handle(GetJobQuery request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Task.FromResult($"job {request.JobId}");
    }
}
