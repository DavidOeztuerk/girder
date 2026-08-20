using Girder.Application.Behaviors;
using Girder.Infrastructure.Tests.Support;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Logging;

/// <summary>
/// What the CQRS pipeline writes about a command it is running.
/// </summary>
/// <remarks>
/// The same question as the HTTP body, one layer up: at debug level the whole
/// command used to be written, sanitised. Sanitising is enumeration — a field
/// called <c>title</c> holding "Termin bei Dr. Weber wegen der Kündigung" is on
/// no list, and it went into the log intact.
/// </remarks>
[Trait("Category", "Unit")]
public class CommandPayloadTests
{
    private sealed record CreateTodoCommand(string Title, string? Note) : IRequest<string>;

    /// <summary>Unmistakable, so it cannot collide with a word in a log message.</summary>
    private const string Sentinel = "qwmzx-response-payload-qwmzx";

    [Fact]
    public async Task No_field_of_a_command_reaches_the_log()
    {
        var log = await RunAsync(new CreateTodoCommand(
            "Termin bei Dr. Weber wegen der Kündigung",
            "Vorher mit Anwalt sprechen"));

        log.Should().NotContain("Dr. Weber")
            .And.NotContain("Kündigung")
            .And.NotContain("Anwalt");
    }

    /// <summary>
    /// What stays is what helps: which command ran, and the shape it had.
    /// </summary>
    [Fact]
    public async Task The_command_and_its_shape_stay()
    {
        var log = await RunAsync(new CreateTodoCommand("something", null));

        log.Should().Contain("CreateTodoCommand");
        log.Should().Contain("Title").And.Contain("Note");
    }

    [Fact]
    public async Task The_answer_is_not_written_out_either()
    {
        var log = await RunAsync(new CreateTodoCommand("something", null));

        log.Should().NotContain(Sentinel);
    }

    private static async Task<string> RunAsync(CreateTodoCommand command)
    {
        var collector = new CollectingLoggerProvider();
        using var factory = LoggerFactory.Create(builder => builder
            .ClearProviders()
            .AddProvider(collector)
            .SetMinimumLevel(LogLevel.Debug));

        var behavior = new LoggingBehavior<CreateTodoCommand, string>(
            factory.CreateLogger<LoggingBehavior<CreateTodoCommand, string>>(),
            Substitute.For<IHttpContextAccessor>());

        await behavior.Handle(command, _ => Task.FromResult(Sentinel), CancellationToken.None);

        return string.Join("\n", collector.Entries);
    }
}
