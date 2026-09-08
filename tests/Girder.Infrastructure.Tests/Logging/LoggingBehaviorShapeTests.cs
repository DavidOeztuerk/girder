using System.Collections;
using Girder.Application.Behaviors;
using Girder.Core.Logging;
using Girder.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Logging;

/// <summary>
/// Logging is not a gate: a command that <see cref="Shape.Of"/> cannot
/// describe must still reach its handler, and a response that cannot be
/// described must still be returned.
/// </summary>
[Trait("Category", "Unit")]
public class LoggingBehaviorShapeTests
{
    [Fact]
    public async Task A_command_carrying_memory_still_reaches_the_handler()
    {
        var called = false;
        var result = await RunAsync(
            new Upload("cv.png", new ReadOnlyMemory<byte>([201, 202, 203])),
            _ =>
            {
                called = true;
                return Task.FromResult("saved");
            });

        called.Should().BeTrue();
        result.Should().Be("saved");
    }

    [Fact]
    public async Task Handle_still_runs_when_the_shape_cannot_be_taken()
    {
        var command = new Unshapeable();
        var takingShape = () => Shape.Of(command);
        takingShape.Should().Throw<Exception>();

        var called = false;
        var result = await RunAsync(command, _ =>
        {
            called = true;
            return Task.FromResult("ok");
        });

        called.Should().BeTrue();
        result.Should().Be("ok");
    }

    [Fact]
    public async Task A_response_that_cannot_be_shaped_is_still_returned()
    {
        var response = new Unshapeable();
        var takingShape = () => Shape.Of(response);
        takingShape.Should().Throw<Exception>();

        var result = await RunAsync(new Ping(), _ => Task.FromResult(response));

        result.Should().BeSameAs(response);
    }

    private static async Task<TResponse> RunAsync<TRequest, TResponse>(
        TRequest request,
        RequestHandlerDelegate<TResponse> next)
        where TRequest : notnull
    {
        var collector = new CollectingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder
            .ClearProviders()
            .AddProvider(collector)
            .SetMinimumLevel(LogLevel.Debug));

        var behavior = new LoggingBehavior<TRequest, TResponse>(
            factory.CreateLogger<LoggingBehavior<TRequest, TResponse>>(),
            Substitute.For<IHttpContextAccessor>());

        return await behavior.Handle(request, next, CancellationToken.None);
    }

    private sealed record Upload(string Name, ReadOnlyMemory<byte> Inhalt) : IRequest<string>;

    private sealed record Ping : IRequest<Unshapeable>;

    /// <summary>
    /// <see cref="Shape.Of"/> enumerates <see cref="IEnumerable"/> before it
    /// walks properties, so a throwing enumerator still blows up after the
    /// ref-struct getter is fixed — the independent case for the behaviour
    /// isolating <c>Shape.Of</c>.
    /// </summary>
    private sealed class Unshapeable : IEnumerable, IRequest<string>
    {
        public IEnumerator GetEnumerator() => throw new NotSupportedException();
    }
}
