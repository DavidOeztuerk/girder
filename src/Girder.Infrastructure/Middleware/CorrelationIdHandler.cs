using Girder.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Middleware;

/// <summary>
/// Carries the current correlation id out on every request an
/// <see cref="HttpClient"/> makes.
/// </summary>
/// <remarks>
/// On the client defaults, so a bare <c>HttpClient</c> from the factory does it
/// without being asked. Forwarding by hand is the kind of step that is
/// remembered on the paths under test and forgotten on the one that matters at
/// three in the morning.
/// </remarks>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A caller who set it meant it — a fan-out that relabels its branches
        // has a reason, and this is not the place to overrule it.
        if (!request.Headers.Contains(CorrelationId.HeaderName)
            && CorrelationId.Current is { Length: > 0 } id)
        {
            request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, id);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Registers correlation-id forwarding for every HTTP client.
/// </summary>
public static class CorrelationIdPropagationExtensions
{
    /// <summary>
    /// Adds <see cref="CorrelationIdHandler"/> to every client the factory
    /// builds.
    /// </summary>
    /// <remarks>
    /// Without this call, an id reaches the services this one talks to only
    /// where something forwards it by hand — the messaging filters do, a bare
    /// <see cref="HttpClient"/> does not, and the logs of the far service then
    /// belong to no story.
    /// </remarks>
    public static IServiceCollection AddCorrelationIdPropagation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTransient<CorrelationIdHandler>();
        services.ConfigureHttpClientDefaults(client =>
            client.AddHttpMessageHandler<CorrelationIdHandler>());

        return services;
    }
}
