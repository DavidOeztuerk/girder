using Girder.Infrastructure.Caching.PlaceholderEvents;

namespace Girder.Infrastructure.Tests.Caching;

[Trait("Category", "Unit")]
public class CacheEventsTests
{
    #region AppointmentCreatedEvent

    [Fact]
    public void AppointmentCreatedEvent_Construction_SetsAppointmentId()
    {
        var evt = new AppointmentCreatedEvent("appt-1");

        evt.AppointmentId.Should().Be("appt-1");
        evt.Id.Should().NotBeNullOrEmpty();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AppointmentCreatedEvent_IdIsUniquePerInstance()
    {
        var evt1 = new AppointmentCreatedEvent("appt-1");
        var evt2 = new AppointmentCreatedEvent("appt-2");

        evt1.Id.Should().NotBe(evt2.Id);
    }

    [Fact]
    public void AppointmentCreatedEvent_ImplementsIDomainEvent()
    {
        var evt = new AppointmentCreatedEvent("appt-1");
        evt.Should().BeAssignableTo<IDomainEvent>();
    }

    #endregion

    #region AppointmentUpdatedEvent

    [Fact]
    public void AppointmentUpdatedEvent_Construction_SetsAppointmentId()
    {
        var evt = new AppointmentUpdatedEvent("appt-2");

        evt.AppointmentId.Should().Be("appt-2");
        evt.Id.Should().NotBeNullOrEmpty();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AppointmentUpdatedEvent_ImplementsIDomainEvent()
    {
        var evt = new AppointmentUpdatedEvent("appt-1");
        evt.Should().BeAssignableTo<IDomainEvent>();
    }

    #endregion

    #region MatchRequestAcceptedEvent

    [Fact]
    public void MatchRequestAcceptedEvent_Construction_SetsMatchId()
    {
        var evt = new MatchRequestAcceptedEvent("match-1");

        evt.MatchId.Should().Be("match-1");
        evt.Id.Should().NotBeNullOrEmpty();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MatchRequestAcceptedEvent_ImplementsIDomainEvent()
    {
        IDomainEvent evt = new MatchRequestAcceptedEvent("match-1");
        evt.Should().NotBeNull();
    }

    #endregion

    #region MatchRequestCreatedEvent

    [Fact]
    public void MatchRequestCreatedEvent_Construction_SetsMatchId()
    {
        var evt = new MatchRequestCreatedEvent("match-2");

        evt.MatchId.Should().Be("match-2");
        evt.Id.Should().NotBeNullOrEmpty();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    #endregion

    #region SkillCreatedEvent

    [Fact]
    public void SkillCreatedEvent_Construction_SetsSkillId()
    {
        var evt = new SkillCreatedEvent("skill-1");

        evt.SkillId.Should().Be("skill-1");
        evt.Id.Should().NotBeNullOrEmpty();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SkillCreatedEvent_ImplementsIDomainEvent()
    {
        var evt = new SkillCreatedEvent("skill-1");
        evt.Should().BeAssignableTo<IDomainEvent>();
    }

    #endregion

    #region SkillDeletedEvent

    [Fact]
    public void SkillDeletedEvent_Construction_SetsSkillId()
    {
        var evt = new SkillDeletedEvent("skill-del-1");

        evt.SkillId.Should().Be("skill-del-1");
        evt.Id.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region SkillUpdatedEvent

    [Fact]
    public void SkillUpdatedEvent_Construction_SetsSkillId()
    {
        var evt = new SkillUpdatedEvent("skill-upd-1");

        evt.SkillId.Should().Be("skill-upd-1");
        evt.Id.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region UserDeletedEvent

    [Fact]
    public void UserDeletedEvent_Construction_SetsUserId()
    {
        var evt = new UserDeletedEvent("user-del-1");

        evt.UserId.Should().Be("user-del-1");
        evt.Id.Should().NotBeNullOrEmpty();
        evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UserDeletedEvent_ImplementsIDomainEvent()
    {
        var evt = new UserDeletedEvent("user-1");
        evt.Should().BeAssignableTo<IDomainEvent>();
    }

    #endregion

    #region IDomainEvent

    [Fact]
    public void IDomainEvent_AllEvents_HaveUniqueIds()
    {
        var events = new IDomainEvent[]
        {
            new AppointmentCreatedEvent("a"),
            new AppointmentUpdatedEvent("b"),
            new MatchRequestAcceptedEvent("c"),
            new MatchRequestCreatedEvent("d"),
            new SkillCreatedEvent("e"),
            new SkillDeletedEvent("f"),
            new SkillUpdatedEvent("g"),
            new UserDeletedEvent("h")
        };

        var ids = events.Select(e => e.Id).ToList();
        ids.Distinct().Should().HaveCount(ids.Count);
    }

    [Fact]
    public void IDomainEvent_AllEvents_HaveOccurredAtSet()
    {
        IDomainEvent[] events =
        [
            new AppointmentCreatedEvent("a"),
            new AppointmentUpdatedEvent("b"),
            new MatchRequestAcceptedEvent("c"),
            new MatchRequestCreatedEvent("d"),
            new SkillCreatedEvent("e"),
            new SkillDeletedEvent("f"),
            new SkillUpdatedEvent("g"),
            new UserDeletedEvent("h")
        ];

        foreach (var evt in events)
        {
            evt.OccurredAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }
    }

    #endregion
}
