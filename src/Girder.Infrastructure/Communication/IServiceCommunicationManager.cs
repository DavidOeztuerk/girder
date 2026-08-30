namespace Girder.Infrastructure.Communication;

public interface IServiceCommunicationManager
{
    /// <summary>
    /// Fetches from another service, and reports what it answered.
    /// </summary>
    /// <remarks>
    /// A status outside 2xx is an answer, not a failure: it arrives in
    /// <see cref="ServiceResponse{T}.Status"/> with the body beside it. Only a
    /// call that never got an answer at all throws.
    /// </remarks>
    Task<ServiceResponse<TResponse>> GetAsync<TResponse>(
        string serviceName,
        string endpoint,
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? headers = null)
        where TResponse : class;

    /// <inheritdoc cref="GetAsync{TResponse}" />
    Task<ServiceResponse<TResponse>> SendRequestAsync<TRequest, TResponse>(
        string serviceName,
        string endpoint,
        TRequest request,
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? headers = null)
        where TRequest : class
        where TResponse : class;

    Task PublishEventAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : class;

    Task<bool> CheckServiceHealthAsync(string serviceName, CancellationToken cancellationToken = default);

    Task<Dictionary<string, ServiceEndpointInfo>> DiscoverServiceEndpointsAsync(
        string serviceName,
        CancellationToken cancellationToken = default);
}

public record ServiceEndpointInfo(
    string Name,
    string Path,
    string Method,
    bool RequiresAuth,
    Dictionary<string, string>? Metadata = null
);
