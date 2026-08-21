using MediatR;

namespace Girder.Fixtures.PlainCqrs;

/// <summary>A request that neither caches nor invalidates anything.</summary>
public sealed record Ping(string Wort) : IRequest<string>;

/// <summary>Answers <see cref="Ping"/> with the word it was given.</summary>
public sealed class PingHandler : IRequestHandler<Ping, string>
{
    public Task<string> Handle(Ping request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Wort);
}
