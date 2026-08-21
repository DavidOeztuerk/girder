using Girder.Application.Interfaces;
using MediatR;

namespace Girder.Fixtures.CachingCqrs;

/// <summary>A command that invalidates what <see cref="GetJobQuery"/> cached.</summary>
public sealed record UpdateJobCommand(string JobId) : IRequest<string>, ICacheInvalidatingCommand
{
    public string[] InvalidationPatterns => ["job:*"];
}

/// <summary>Does nothing but succeed.</summary>
public sealed class UpdateJobCommandHandler : IRequestHandler<UpdateJobCommand, string>
{
    public Task<string> Handle(UpdateJobCommand request, CancellationToken cancellationToken) =>
        Task.FromResult($"updated {request.JobId}");
}
