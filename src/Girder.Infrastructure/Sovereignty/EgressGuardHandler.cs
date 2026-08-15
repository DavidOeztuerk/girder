using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Sovereignty;

/// <summary>
/// Refuses outbound calls to hosts the <see cref="IEgressPolicy"/> does not allow.
/// </summary>
/// <remarks>
/// Fails the call rather than logging it. A warning about data that already left
/// is a record, not a control.
/// </remarks>
public sealed class EgressGuardHandler : DelegatingHandler
{
    private readonly IEgressPolicy _policy;
    private readonly ILogger<EgressGuardHandler> _logger;

    public EgressGuardHandler(IEgressPolicy policy, ILogger<EgressGuardHandler> logger)
    {
        _policy = policy;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var destination = request.RequestUri;

        if (destination is not null && !_policy.IsAllowed(destination))
        {
            _logger.LogError(
                "Blocked an outbound call to {Host}, which is not a declared egress destination",
                destination.Host);

            throw new EgressDeniedException(destination, _policy.DeclaredHosts);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
