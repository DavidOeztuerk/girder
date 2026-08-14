namespace Infrastructure.Communication;

public interface IServiceCommunicationManager
{
    Task<TResponse?> GetAsync<TResponse>(
        string serviceName,
        string endpoint,
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? headers = null)
        where TResponse : class;

    Task<TResponse?> SendRequestAsync<TRequest, TResponse>(
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
