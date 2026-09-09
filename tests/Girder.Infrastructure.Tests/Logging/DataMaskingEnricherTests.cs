using System.Collections;
using FluentAssertions;
using Girder.Infrastructure.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Serilog;
using Serilog.Events;
using Serilog.Parsing;
using Xunit;

namespace Girder.Infrastructure.Tests.Logging;

[Trait("Category", "Unit")]
public class DataMaskingEnricherTests
{
    private readonly DataMaskingEnricher _enricher = new();

    [Theory]
    [InlineData("Password", "supersecret")]
    [InlineData("password", "supersecret")]
    [InlineData("UserPassword", "supersecret")]
    [InlineData("Token", "jwt-token-value")]
    [InlineData("AccessToken", "jwt-token-value")]
    [InlineData("RefreshToken", "jwt-token-value")]
    [InlineData("Secret", "topsecret")]
    [InlineData("ClientSecret", "topsecret")]
    [InlineData("Authorization", "Bearer eyJhbGciOi...")]
    [InlineData("authorization", "Bearer eyJhbGciOi...")]
    [InlineData("IBAN", "DE89370400440532013000")]
    [InlineData("iban", "DE89370400440532013000")]
    public void Sensitive_scalar_properties_are_masked(string key, string value)
    {
        var logEvent = CreateLogEvent(new LogEventProperty(key, new ScalarValue(value)));

        _enricher.Enrich(logEvent, null!);

        logEvent.Properties.Should().ContainKey(key);
        var masked = logEvent.Properties[key] as ScalarValue;
        masked.Should().NotBeNull();
        masked!.Value.Should().Be(DataMaskingEnricher.Mask);
    }

    /// <summary>
    /// Names of software are not names of people.
    /// </summary>
    /// <remarks>
    /// The exact-match rule is what keeps these readable — every one of them
    /// contains a sensitive word as a substring, and a log with them redacted is
    /// one nobody can follow.
    /// </remarks>
    [Fact]
    public void Non_sensitive_properties_are_not_masked()
    {
        var logEvent = CreateLogEvent(
            new LogEventProperty("RequestId", new ScalarValue("12345")),
            new LogEventProperty("ServiceName", new ScalarValue("identity")),
            new LogEventProperty("SecretName", new ScalarValue("jwt-signing-key")),
            new LogEventProperty("TokenId", new ScalarValue("jti-42")),
            new LogEventProperty("CancellationToken", new ScalarValue("None")));

        _enricher.Enrich(logEvent, null!);

        ((ScalarValue)logEvent.Properties["RequestId"]).Value.Should().Be("12345");
        ((ScalarValue)logEvent.Properties["ServiceName"]).Value.Should().Be("identity");
        ((ScalarValue)logEvent.Properties["SecretName"]).Value.Should().Be("jwt-signing-key");
        ((ScalarValue)logEvent.Properties["TokenId"]).Value.Should().Be("jti-42");
        ((ScalarValue)logEvent.Properties["CancellationToken"]).Value.Should().Be("None");
    }

    /// <summary>
    /// A person's name, on the other hand, is on the list — the same list the
    /// CQRS behaviour and the HTTP middleware read.
    /// </summary>
    [Theory]
    [InlineData("Username")]
    [InlineData("Email")]
    [InlineData("City")]
    public void Personal_data_is_masked_because_the_shared_list_says_so(string key)
    {
        var logEvent = CreateLogEvent(new LogEventProperty(key, new ScalarValue("alice")));

        _enricher.Enrich(logEvent, null!);

        ((ScalarValue)logEvent.Properties[key]).Value.Should().Be(DataMaskingEnricher.Mask);
    }

    [Fact]
    public void Nested_structure_properties_are_masked_recursively()
    {
        var nestedProperties = new List<LogEventProperty>
        {
            new("Username", new ScalarValue("bob")),
            new("Password", new ScalarValue("secret123")),
            new("Profile", new StructureValue(
            [
                new LogEventProperty("IBAN", new ScalarValue("DE12345678")),
                new LogEventProperty("City", new ScalarValue("Berlin"))
            ]))
        };

        var userStructure = new StructureValue(nestedProperties);
        var logEvent = CreateLogEvent(new LogEventProperty("User", userStructure));

        _enricher.Enrich(logEvent, null!);

        var maskedStructure = logEvent.Properties["User"] as StructureValue;
        maskedStructure.Should().NotBeNull();

        var props = maskedStructure!.Properties.ToDictionary(p => p.Name, p => p.Value);
        ((ScalarValue)props["Username"]).Value.Should().Be(DataMaskingEnricher.Mask);
        ((ScalarValue)props["Password"]).Value.Should().Be(DataMaskingEnricher.Mask);

        var innerProfile = props["Profile"] as StructureValue;
        innerProfile.Should().NotBeNull();
        var innerProps = innerProfile!.Properties.ToDictionary(p => p.Name, p => p.Value);
        ((ScalarValue)innerProps["IBAN"]).Value.Should().Be(DataMaskingEnricher.Mask);
        ((ScalarValue)innerProps["City"]).Value.Should().Be(DataMaskingEnricher.Mask);
    }

    [Fact]
    public void Dictionary_elements_with_sensitive_keys_are_masked()
    {
        var elements = new Dictionary<ScalarValue, LogEventPropertyValue>
        {
            [new ScalarValue("Authorization")] = new ScalarValue("Bearer token123"),
            [new ScalarValue("NormalKey")] = new ScalarValue("NormalValue")
        };

        var dictValue = new DictionaryValue(elements);
        var logEvent = CreateLogEvent(new LogEventProperty("Headers", dictValue));

        _enricher.Enrich(logEvent, null!);

        var maskedDict = logEvent.Properties["Headers"] as DictionaryValue;
        maskedDict.Should().NotBeNull();

        var authKey = maskedDict!.Elements.Keys.First(k => k.Value?.ToString() == "Authorization");
        var normalKey = maskedDict.Elements.Keys.First(k => k.Value?.ToString() == "NormalKey");

        ((ScalarValue)maskedDict.Elements[authKey]).Value.Should().Be(DataMaskingEnricher.Mask);
        ((ScalarValue)maskedDict.Elements[normalKey]).Value.Should().Be("NormalValue");
    }

    [Fact]
    public void Sequences_with_structures_are_masked_recursively()
    {
        var item1 = new StructureValue([new LogEventProperty("Token", new ScalarValue("tok-1"))]);
        var item2 = new StructureValue([new LogEventProperty("ItemId", new ScalarValue("item-2"))]);

        var seq = new SequenceValue([item1, item2]);
        var logEvent = CreateLogEvent(new LogEventProperty("Items", seq));

        _enricher.Enrich(logEvent, null!);

        var maskedSeq = logEvent.Properties["Items"] as SequenceValue;
        maskedSeq.Should().NotBeNull();

        var firstStruct = maskedSeq!.Elements[0] as StructureValue;
        ((ScalarValue)firstStruct!.Properties[0].Value).Value.Should().Be(DataMaskingEnricher.Mask);

        var secondStruct = maskedSeq.Elements[1] as StructureValue;
        ((ScalarValue)secondStruct!.Properties[0].Value).Value.Should().Be("item-2");
    }

    [Fact]
    public void WithDataMasking_masks_sensitive_properties_in_serilog_pipeline()
    {
        var sink = new CollectingSink();
        var logger = new LoggerConfiguration()
            .Enrich.WithDataMasking()
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("User details: {Password}", "secret-unmasked-value");

        sink.Events.Should().HaveCount(1);
        var evt = sink.Events[0];
        evt.Properties.Should().ContainKey("Password");
        var val = evt.Properties["Password"] as ScalarValue;
        val.Should().NotBeNull();
        val!.Value.Should().Be(DataMaskingEnricher.Mask);
    }

    private sealed class CollectingSink : Serilog.Core.ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    private static LogEvent CreateLogEvent(params LogEventProperty[] properties)
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplate("test", []),
            properties);
    }
}
