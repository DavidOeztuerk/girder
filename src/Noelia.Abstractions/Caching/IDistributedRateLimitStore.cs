namespace Noelia.Abstractions.Caching;

/// <summary>
/// Interface for distributed rate limiting storage
/// </summary>
public interface IDistributedRateLimitStore
{
    /// <summary>
    /// Get current count for a rate limit key
    /// </summary>
    Task<long> GetCountAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment count for a rate limit key with expiration
    /// </summary>
    Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a key exists
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Set expiration for a key
    /// </summary>
    Task<bool> ExpireAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get time to live for a key
    /// </summary>
    Task<TimeSpan?> GetTimeToLiveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a key
    /// </summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts one request against <paramref name="key"/> within a sliding
    /// <paramref name="window"/> and reports whether it stays under
    /// <paramref name="limit"/>.
    /// </summary>
    /// <remarks>
    /// Counting and deciding happen as one indivisible step: concurrent callers
    /// at the limit must not all be allowed through. Implementations that
    /// cannot guarantee that are not valid implementations of this port.
    /// </remarks>
    Task<WindowCheckResult> SlidingWindowIncrementAsync(
        string key, 
        int limit, 
        TimeSpan window, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads bounded, fingerprinted counter observations without exposing the
    /// subject-bearing store keys. Providers that cannot inspect safely report
    /// unavailable.
    /// </summary>
    Task<RateLimitInspection> InspectAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(RateLimitInspection.Unavailable);
}

/// <summary>One observed counter with a one-way key fingerprint.</summary>
public sealed record RateLimitCounterEntry(
    string KeyFingerprint,
    long CurrentCount,
    int? Limit,
    bool IsRejected,
    DateTimeOffset ObservedAt);

/// <summary>A bounded view of rate-limit activity for one store.</summary>
public sealed record RateLimitInspection(
    bool IsAvailable,
    bool IsInstanceScoped,
    IReadOnlyList<RateLimitCounterEntry> Counters)
{
    /// <summary>A provider with no safe counter read model.</summary>
    public static RateLimitInspection Unavailable { get; } =
        new(false, false, Array.Empty<RateLimitCounterEntry>());
}

/// <summary>
/// What one sliding window says about one request.
/// </summary>
/// <remarks>
/// One counter over one window, and nothing about the decision built from
/// several of them: a request is refused only if every window it was checked
/// against agrees, and that verdict belongs to whoever asked all of them.
/// </remarks>
public record WindowCheckResult
{
    /// <summary>
    /// Whether the backing store could make this decision.
    /// </summary>
    /// <remarks>
    /// False is not the same as an exhausted limit. It means the caller is
    /// applying its declared outage policy instead of a measured count.
    /// </remarks>
    public bool IsStoreAvailable { get; init; } = true;

    /// <summary>
    /// Whether the request is allowed
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Current count in the window
    /// </summary>
    public long CurrentCount { get; init; }

    /// <summary>
    /// Rate limit for the window
    /// </summary>
    public int Limit { get; init; }

    /// <summary>
    /// Time remaining until window resets
    /// </summary>
    public TimeSpan? ResetTime { get; init; }

    /// <summary>
    /// Number of requests remaining in the current window
    /// </summary>
    public long RemainingRequests => Math.Max(0, Limit - CurrentCount);
}
