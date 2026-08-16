using Girder.Abstractions.Security;
using Girder.Redis.Security;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// Starts one Redis container for the whole class.
/// </summary>
/// <remarks>
/// Fails, rather than skipping, when no container runtime is reachable. A
/// silently skipped integration suite is indistinguishable from a passing one,
/// and this is the only place the Redis contract is actually exercised.
/// </remarks>
public sealed class RedisFixture : IAsyncLifetime
{
    private RedisContainer? _container;

    /// <summary>Null when no container runtime was reachable.</summary>
    public IConnectionMultiplexer? Connection { get; private set; }

    /// <summary>Why the container did not start, if it did not.</summary>
    public Exception? StartupFailure { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _container = new RedisBuilder().WithImage("redis:8-alpine").Build();
            await _container.StartAsync();
            Connection = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        }
        catch (Exception ex)
        {
            StartupFailure = ex;
        }
    }

    public async Task DisposeAsync()
    {
        if (Connection is not null) { await Connection.CloseAsync(); Connection.Dispose(); }
        if (_container is not null) await _container.DisposeAsync();
    }
}

/// <summary>
/// The same contract as the in-process store, answered by Redis.
/// </summary>
/// <remarks>
/// This is where the port stops being a claim. The two implementations reach
/// the same verdicts by different means — a dictionary with a lock on one side,
/// a batch and a Lua script on the other — and only running both against one
/// suite shows whether they really agree.
/// </remarks>
[Trait("Category", "Integration")]
public class RedisTokenRevocationConformanceTests
    : TokenRevocationEvaluatorConformance, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public RedisTokenRevocationConformanceTests(RedisFixture fixture)
    {
        _fixture = fixture;

        if (fixture.Connection is null)
        {
            throw new InvalidOperationException(
                "The Redis conformance suite needs a container runtime. Start Docker and run again.",
                fixture.StartupFailure);
        }
    }

    protected override (ITokenRevocationEvaluator, ITokenRevocationWriter) CreateStore()
    {
        var store = new RedisTokenRevocationStore(_fixture.Connection!, TimeSpan.FromHours(24));
        return (store, store);
    }
}
