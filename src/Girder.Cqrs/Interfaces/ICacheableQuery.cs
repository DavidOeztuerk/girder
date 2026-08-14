namespace CQRS.Interfaces;

/// <summary>
/// Interface for commands that can be cached
/// </summary>
public interface ICacheableQuery
{
    string CacheKey { get; }
    TimeSpan CacheDuration { get; }
}
