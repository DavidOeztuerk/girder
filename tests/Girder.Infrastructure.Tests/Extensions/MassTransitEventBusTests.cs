using Infrastructure.Extensions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
public class MassTransitEventBusTests
{
    private class TestEvent
    {
        public string Message { get; set; } = string.Empty;
    }

    [Fact]
    public async Task PublishAsync_PublishesEventToEndpoint()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);
        var @event = new TestEvent { Message = "Hello" };

        await eventBus.PublishAsync(@event);

        await publishEndpoint.Received(1).Publish(
            Arg.Is<TestEvent>(e => e.Message == "Hello"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenPublishSucceeds_DoesNotThrow()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);

        var act = async () => await eventBus.PublishAsync(new TestEvent { Message = "Test" });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishAsync_WhenPublishFails_ThrowsException()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint.Publish(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("RabbitMQ unavailable"));

        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);

        var act = async () => await eventBus.PublishAsync(new TestEvent { Message = "Test" });
        await act.Should().ThrowAsync<Exception>().WithMessage("RabbitMQ unavailable");
    }

    [Fact]
    public async Task PublishAsync_WithCancellationToken_PassesTokenToPublish()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);
        var cts = new CancellationTokenSource();

        await eventBus.PublishAsync(new TestEvent(), cts.Token);

        await publishEndpoint.Received(1).Publish(
            Arg.Any<TestEvent>(),
            cts.Token);
    }

    [Fact]
    public async Task PublishAsync_WithDifferentEventTypes_PublishesCorrectType()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var logger = Substitute.For<ILogger<MassTransitEventBus>>();
        var eventBus = new MassTransitEventBus(publishEndpoint, logger);

        await eventBus.PublishAsync("string event");
        await eventBus.PublishAsync(new TestEvent { Message = "second" });

        await publishEndpoint.Received(1).Publish(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await publishEndpoint.Received(1).Publish(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>());
    }
}
