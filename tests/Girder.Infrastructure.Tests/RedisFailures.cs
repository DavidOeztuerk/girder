using StackExchange.Redis;

namespace Girder.Infrastructure.Tests;

/// <summary>
/// Builds the exceptions a Redis client raises when the server is unreachable.
/// </summary>
/// <remarks>
/// Only one <see cref="RedisConnectionException"/> constructor is not obsolete,
/// and it takes five arguments of which four are noise for a test. Wrapping it
/// once keeps that detail out of every test that needs a failing store.
/// </remarks>
internal static class RedisFailures
{
    /// <summary>The connection dropped mid-command.</summary>
    public static RedisConnectionException Unreachable(string message = "test") =>
        new(ConnectionFailureType.SocketClosed, CommandFlags.None, message,
            innerException: null, commandStatus: CommandStatus.Unknown);

    /// <summary>The command was sent but no answer came back.</summary>
    public static RedisConnectionException NoAnswer(string message = "test") =>
        new(ConnectionFailureType.UnableToResolvePhysicalConnection, CommandFlags.None, message,
            innerException: null, commandStatus: CommandStatus.Sent);
}
