using FluentAssertions;
using Girder.Core.Identity;
using Girder.Infrastructure.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Girder.Infrastructure.Tests.Audit;

[Trait("Category", "Unit")]
public class AuditTrailServiceTests
{
    private readonly InMemorySovereignAuditSink _sink = new();
    private readonly AuditTrailService _service;

    public AuditTrailServiceTests()
    {
        _service = new AuditTrailService(_sink, NullLogger<AuditTrailService>.Instance);
    }

    private sealed record Document(string Id, string Title, decimal Amount);

    [Fact]
    public async Task Consecutive_events_are_hash_chained()
    {
        var doc1 = new Document("doc-1", "First Draft", 100m);
        var doc2 = new Document("doc-1", "Second Draft", 150m);

        var event1 = await _service.RecordAsync(
            actorId: "usr_alice",
            capacity: "as self",
            action: "Create",
            resource: "Document/doc-1",
            before: (Document?)null,
            after: doc1,
            correlationId: "corr-1");

        var event2 = await _service.RecordAsync(
            actorId: "usr_bob",
            capacity: "for company 100",
            action: "Update",
            resource: "Document/doc-1",
            before: doc1,
            after: doc2,
            correlationId: "corr-2");

        var event3 = await _service.RecordAsync(
            actorId: "usr_charlie",
            capacity: "as self",
            action: "Delete",
            resource: "Document/doc-1",
            before: doc2,
            after: (Document?)null,
            correlationId: "corr-3");

        event1.PreviousHash.Should().BeNull();
        event1.Hash.Should().NotBeNullOrWhiteSpace();
        event1.VerifyHash().Should().BeTrue();

        event2.PreviousHash.Should().Be(event1.Hash);
        event2.Hash.Should().NotBeNullOrWhiteSpace();
        event2.VerifyHash().Should().BeTrue();

        event3.PreviousHash.Should().Be(event2.Hash);
        event3.Hash.Should().NotBeNullOrWhiteSpace();
        event3.VerifyHash().Should().BeTrue();

        _sink.Events.Should().HaveCount(3);
        _sink.EventsOf<Document>().Should().HaveCount(3);
    }

    [Fact]
    public async Task Tampering_with_an_event_breaks_its_hash_verification()
    {
        var doc = new Document("doc-secret", "Invoice", 1000m);

        var auditEvent = await _service.RecordAsync(
            actorId: "usr_eve",
            capacity: "as self",
            action: "Create",
            resource: "Document/doc-secret",
            before: (Document?)null,
            after: doc);

        auditEvent.VerifyHash().Should().BeTrue();

        var tamperedAction = auditEvent with { Action = "TamperedAction" };
        tamperedAction.VerifyHash().Should().BeFalse();

        var tamperedActor = auditEvent with { ActorId = "malicious_actor" };
        tamperedActor.VerifyHash().Should().BeFalse();

        var tamperedState = auditEvent with { AfterStateJson = "{\"title\":\"Fake\"}" };
        tamperedState.VerifyHash().Should().BeFalse();

        var tamperedPrevHash = auditEvent with { PreviousHash = "broken_previous_hash" };
        tamperedPrevHash.VerifyHash().Should().BeFalse();
    }

    [Fact]
    public async Task Capacity_overload_records_string_representation()
    {
        var tenant = TenantId.New();
        var capacity = new Capacity.ForCompany(tenant);

        var auditEvent = await _service.RecordAsync(
            actorId: "usr_admin",
            capacity: capacity,
            action: "Publish",
            resource: "Job/42",
            before: "Draft",
            after: "Published",
            correlationId: "corr-tenant");

        auditEvent.Capacity.Should().Be($"for company {tenant}");
        auditEvent.VerifyHash().Should().BeTrue();
    }
}
