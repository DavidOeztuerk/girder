using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Infrastructure.Communication.Deduplication;

/// <summary>
/// Request deduplicator implementation using Single Flight pattern
/// </summary>
public class RequestDeduplicator : IRequestDeduplicator
{
    private readonly ILogger<RequestDeduplicator> _logger;
    private readonly ConcurrentDictionary<string, Task<object?>> _inflightRequests = new();

    // Statistics
    private long _totalRequests = 0;
    private long _deduplicatedRequests = 0;
    private long _uniqueRequests = 0;
    private readonly object _statsLock = new();

    public RequestDeduplicator(ILogger<RequestDeduplicator> logger)
    {
        _logger = logger;
    }

    public async Task<T?> ExecuteAsync<T>(string key, Func<Task<T?>> operation, CancellationToken cancellationToken = default) where T : class
    {
        IncrementTotalRequests();

        // Try to get existing in-flight request
        if (_inflightRequests.TryGetValue(key, out var existingTask))
        {
            _logger.LogDebug("Deduplicating request for key: {Key}, waiting for existing request", key);
            IncrementDeduplicatedRequests();

            try
            {
                var result = await existingTask;
                return result as T;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Existing request failed for key: {Key}", key);
                throw;
            }
        }

        // Create new task for this request
        var tcs = new TaskCompletionSource<object?>();
        var taskToExecute = tcs.Task;

        // Try to add our task to the dictionary
        if (!_inflightRequests.TryAdd(key, taskToExecute))
        {
            // Someone else added their task first, use theirs
            if (_inflightRequests.TryGetValue(key, out var racedTask))
            {
                _logger.LogDebug("Race condition detected for key: {Key}, using winner's request", key);
                IncrementDeduplicatedRequests();

                try
                {
                    var result = await racedTask;
                    return result as T;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Raced request failed for key: {Key}", key);
                    throw;
                }
            }
        }

        // We won the race, execute the operation
        IncrementUniqueRequests();
        _logger.LogDebug("Executing unique request for key: {Key}", key);

        try
        {
            var result = await operation();
            tcs.SetResult(result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Request execution failed for key: {Key}", key);
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            // Remove from in-flight requests
            _inflightRequests.TryRemove(key, out _);
        }
    }

    public DeduplicationStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new DeduplicationStatistics
            {
                TotalRequests = _totalRequests,
                DeduplicatedRequests = _deduplicatedRequests,
                UniqueRequests = _uniqueRequests,
                CurrentInflightRequests = _inflightRequests.Count
            };
        }
    }

    public void ClearInflightRequests()
    {
        _inflightRequests.Clear();
        _logger.LogInformation("Cleared all in-flight requests");
    }

    private void IncrementTotalRequests()
    {
        lock (_statsLock)
        {
            _totalRequests++;
        }
    }

    private void IncrementDeduplicatedRequests()
    {
        lock (_statsLock)
        {
            _deduplicatedRequests++;
        }
    }

    private void IncrementUniqueRequests()
    {
        lock (_statsLock)
        {
            _uniqueRequests++;
        }
    }
}
