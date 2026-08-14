using Girder.Infrastructure.Messaging;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Messaging;

[Trait("Category", "Unit")]
public class CorrelationIdConsumeFilterTests
{
    private readonly ILogger<CorrelationIdConsumeFilter<TestMessage>> _logger =
        Substitute.For<ILogger<CorrelationIdConsumeFilter<TestMessage>>>();

    [Fact]
    public async Task Send_WithCorrelationIdHeader_PropagatesCorrelationId()
    {
        var filter = new CorrelationIdConsumeFilter<TestMessage>(_logger);
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        var headers = Substitute.For<Headers>();
        headers.Get<string>("X-Correlation-ID").Returns("test-correlation-123");
        context.Headers.Returns(headers);
        context.MessageId.Returns(Guid.NewGuid());

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_WithoutCorrelationIdHeader_UsesConversationId()
    {
        var filter = new CorrelationIdConsumeFilter<TestMessage>(_logger);
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        var headers = Substitute.For<Headers>();
        headers.Get<string>("X-Correlation-ID").Returns((string?)null);
        context.Headers.Returns(headers);
        context.ConversationId.Returns(Guid.NewGuid());
        context.MessageId.Returns(Guid.NewGuid());

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_WithNoIds_GeneratesNewCorrelationId()
    {
        var filter = new CorrelationIdConsumeFilter<TestMessage>(_logger);
        var context = Substitute.For<ConsumeContext<TestMessage>>();
        var headers = Substitute.For<Headers>();
        headers.Get<string>("X-Correlation-ID").Returns((string?)null);
        context.Headers.Returns(headers);
        context.ConversationId.Returns((Guid?)null);
        context.MessageId.Returns((Guid?)null);

        var next = Substitute.For<IPipe<ConsumeContext<TestMessage>>>();

        await filter.Send(context, next);

        await next.Received(1).Send(context);
    }

    [Fact]
    public void Probe_CreatesFilterScope()
    {
        var filter = new CorrelationIdConsumeFilter<TestMessage>(_logger);
        var probeContext = Substitute.For<ProbeContext>();

        filter.Probe(probeContext);

        probeContext.Received(1).CreateFilterScope("correlationIdConsume");
    }
}

[Trait("Category", "Unit")]
public class CorrelationIdPublishFilterTests
{
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
    private readonly ILogger<CorrelationIdPublishFilter<TestMessage>> _logger =
        Substitute.For<ILogger<CorrelationIdPublishFilter<TestMessage>>>();

    [Fact]
    public async Task Send_WithCorrelationIdInContext_SetsHeader()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = "publish-correlation-456";
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var filter = new CorrelationIdPublishFilter<TestMessage>(_httpContextAccessor, _logger);
        var context = Substitute.For<PublishContext<TestMessage>>();
        var sendHeaders = Substitute.For<SendHeaders>();
        context.Headers.Returns(sendHeaders);

        var next = Substitute.For<IPipe<PublishContext<TestMessage>>>();

        await filter.Send(context, next);

        sendHeaders.Received(1).Set("X-Correlation-ID", "publish-correlation-456");
        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_WithoutCorrelationId_DoesNotSetHeader()
    {
        var httpContext = new DefaultHttpContext();
        _httpContextAccessor.HttpContext.Returns(httpContext);

        var filter = new CorrelationIdPublishFilter<TestMessage>(_httpContextAccessor, _logger);
        var context = Substitute.For<PublishContext<TestMessage>>();
        var sendHeaders = Substitute.For<SendHeaders>();
        context.Headers.Returns(sendHeaders);

        var next = Substitute.For<IPipe<PublishContext<TestMessage>>>();

        await filter.Send(context, next);

        sendHeaders.DidNotReceive().Set(Arg.Any<string>(), Arg.Any<string>());
        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_WithNullHttpContext_DoesNotSetHeader()
    {
        _httpContextAccessor.HttpContext.Returns((HttpContext?)null);

        var filter = new CorrelationIdPublishFilter<TestMessage>(_httpContextAccessor, _logger);
        var context = Substitute.For<PublishContext<TestMessage>>();
        var sendHeaders = Substitute.For<SendHeaders>();
        context.Headers.Returns(sendHeaders);

        var next = Substitute.For<IPipe<PublishContext<TestMessage>>>();

        await filter.Send(context, next);

        sendHeaders.DidNotReceive().Set(Arg.Any<string>(), Arg.Any<string>());
        await next.Received(1).Send(context);
    }

    [Fact]
    public void Probe_CreatesFilterScope()
    {
        var filter = new CorrelationIdPublishFilter<TestMessage>(_httpContextAccessor, _logger);
        var probeContext = Substitute.For<ProbeContext>();

        filter.Probe(probeContext);

        probeContext.Received(1).CreateFilterScope("correlationIdPublish");
    }
}

public class TestMessage
{
    public string Id { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
