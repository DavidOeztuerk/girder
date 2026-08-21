using Girder.Application.Interfaces;
using MediatR;

namespace Girder.Fixtures.CachingCqrs;

/// <summary>Invalidates cache entries but declares no ETag patterns.</summary>
public sealed record ArchiveJobCommand(string JobId) : IRequest<string>, ICacheInvalidatingCommand
{
    public string[] InvalidationPatterns => ["job:*"];
}

/// <summary>Does nothing but succeed.</summary>
public sealed class ArchiveJobCommandHandler : IRequestHandler<ArchiveJobCommand, string>
{
    public Task<string> Handle(ArchiveJobCommand request, CancellationToken cancellationToken) =>
        Task.FromResult($"archived {request.JobId}");
}
