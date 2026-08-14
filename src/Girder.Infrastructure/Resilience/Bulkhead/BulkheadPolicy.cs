using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Infrastructure.Resilience.Bulkhead;

/// <summary>
/// Bulkhead policy implementation
/// </summary>
public class BulkheadPolicy : IBulkheadPolicy
{
    private readonly BulkheadOptions _options;
    private readonly ILogger<BulkheadPolicy> _logger;
    private readonly string _name;
    private readonly SemaphoreSlim _semaphore;
    private readonly Channel<PendingRequest> _queue;

    // Statistics
    private long _totalExecutions = 0;
    private long _rejectedExecutions = 0;
    private long _queuedExecutions = 0;
    private int _currentParallelization = 0;
    private readonly object _statsLock = new();

    public BulkheadPolicy(BulkheadOptions options, ILogger<BulkheadPolicy> logger, string name = "Default")
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _name = name;

        _semaphore = new SemaphoreSlim(options.MaxParallelization, options.MaxParallelization);
        _queue = Channel.CreateBounded<PendingRequest>(new BoundedChannelOptions(options.MaxQueuedActions)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        ValidateOptions();
        StartQueueProcessor();
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(operation, () => throw new BulkheadRejectedException($"Bulkhead '{_name}' is full"), cancellationToken);
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, Func<Task<T>> fallback, CancellationToken cancellationToken = default)
    {
        IncrementTotalExecutions();

        // Try to acquire semaphore immediately
        if (_semaphore.Wait(0))
        {
            try
            {
                IncrementCurrentParallelization();
                return await operation();
            }
            finally
            {
                DecrementCurrentParallelization();
                _semaphore.Release();
            }
        }

        // Semaphore full, try to queue
        if (_options.MaxQueuedActions > 0)
        {
            var tcs = new TaskCompletionSource<T>();
            var pendingRequest = new PendingRequest
            {
                Execute = async () =>
                {
                    try
                    {
                        var result = await operation();
                        tcs.SetResult(result);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                },
                CancellationToken = cancellationToken
            };

            if (_queue.Writer.TryWrite(pendingRequest))
            {
                IncrementQueuedExecutions();
                _logger.LogDebug("Request queued in bulkhead '{Name}'", _name);
                return await tcs.Task;
            }
        }

        // Both semaphore and queue are full, execute fallback
        IncrementRejectedExecutions();
        _logger.LogWarning("Bulkhead '{Name}' rejected request (parallel: {Current}/{Max}, queued: {Queued}/{MaxQueued})",
            _name, _currentParallelization, _options.MaxParallelization, _queue.Reader.Count, _options.MaxQueuedActions);

        return await fallback();
    }

    public BulkheadStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            return new BulkheadStatistics
            {
                MaxParallelization = _options.MaxParallelization,
                MaxQueuedActions = _options.MaxQueuedActions,
                CurrentParallelization = _currentParallelization,
                CurrentQueuedActions = _queue.Reader.Count,
                TotalExecutions = _totalExecutions,
                RejectedExecutions = _rejectedExecutions,
                QueuedExecutions = _queuedExecutions
            };
        }
    }

    public void ResetStatistics()
    {
        lock (_statsLock)
        {
            _totalExecutions = 0;
            _rejectedExecutions = 0;
            _queuedExecutions = 0;
        }

        _logger.LogInformation("Reset statistics for bulkhead '{Name}'", _name);
    }

    private void StartQueueProcessor()
    {
        Task.Run(async () =>
        {
            await foreach (var pendingRequest in _queue.Reader.ReadAllAsync())
            {
                if (pendingRequest.CancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                await _semaphore.WaitAsync(pendingRequest.CancellationToken);
                try
                {
                    IncrementCurrentParallelization();
                    await pendingRequest.Execute();
                }
                finally
                {
                    DecrementCurrentParallelization();
                    _semaphore.Release();
                }
            }
        });
    }

    private void IncrementTotalExecutions()
    {
        lock (_statsLock)
        {
            _totalExecutions++;
        }
    }

    private void IncrementRejectedExecutions()
    {
        lock (_statsLock)
        {
            _rejectedExecutions++;
        }
    }

    private void IncrementQueuedExecutions()
    {
        lock (_statsLock)
        {
            _queuedExecutions++;
        }
    }

    private void IncrementCurrentParallelization()
    {
        lock (_statsLock)
        {
            _currentParallelization++;
        }
    }

    private void DecrementCurrentParallelization()
    {
        lock (_statsLock)
        {
            _currentParallelization--;
        }
    }

    private void ValidateOptions()
    {
        if (_options.MaxParallelization <= 0)
        {
            throw new ArgumentException("MaxParallelization must be greater than 0");
        }

        if (_options.MaxQueuedActions < 0)
        {
            throw new ArgumentException("MaxQueuedActions must be non-negative");
        }
    }

    private class PendingRequest
    {
        public Func<Task> Execute { get; set; } = null!;
        public CancellationToken CancellationToken { get; set; }
    }
}

/// <summary>
/// Bulkhead configuration options
/// </summary>
public class BulkheadOptions
{
    /// <summary>
    /// Maximum number of parallel executions
    /// </summary>
    public int MaxParallelization { get; set; } = 100;

    /// <summary>
    /// Maximum number of queued actions
    /// </summary>
    public int MaxQueuedActions { get; set; } = 50;
}
