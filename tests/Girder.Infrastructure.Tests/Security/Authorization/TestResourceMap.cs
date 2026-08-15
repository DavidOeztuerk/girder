using Girder.Infrastructure.Security.Authorization;

namespace Girder.Infrastructure.Tests.Security.Authorization;

/// <summary>
/// A resource map for the resolution tests.
/// </summary>
/// <remarks>
/// The vocabulary belongs to the tests, not to Girder: the library resolves
/// whatever an application declares, and these tests exercise the resolution
/// rules against one such declaration.
/// </remarks>
internal static class TestResourceMap
{
    public const string User = "User";
    public const string Skill = "Skill";
    public const string Match = "Match";
    public const string Appointment = "Appointment";
    public const string Videocall = "Videocall";
    public const string Notification = "Notification";
    public const string System = "System";

    public static readonly IResourceMap Instance = new ResourceMapBuilder()
        // Route parameters that name a resource unambiguously. "id" and "userId"
        // are deliberately absent: they say nothing about which resource is meant.
        .RouteParameter(Appointment, "appointmentId")
        .RouteParameter(Match, "matchId", "requestId")
        .RouteParameter(Skill, "skillId", "listingId", "topicId")
        .RouteParameter(Videocall, "sessionId")
        .RouteParameter(Notification, "notificationId", "templateId")
        .RouteParameter(System, "alertId")

        .PathSegment(User, "users", "user", "auth")
        .PathSegment(Skill, "skills", "skill", "listings", "listing")
        .PathSegment(Match, "matches", "match", "match-requests")
        .PathSegment(Appointment, "appointments", "appointment", "reviews")
        .PathSegment(Videocall, "videocall", "videocalls", "calls")
        .PathSegment(Notification, "notifications", "notification", "preferences", "reminders", "templates")
        .PathSegment(System, "admin", "system")

        // Which parameter carries the id of each resource, most specific first.
        .IdParameters(Appointment, "appointmentId")
        .IdParameters(Match, "matchId", "requestId")
        .IdParameters(Skill, "skillId", "listingId", "topicId")
        .IdParameters(User, "userId")
        .IdParameters(Videocall, "sessionId")
        .IdParameters(Notification, "notificationId", "templateId")
        .IdParameters(System, "alertId", "userId")

        // Named only so that generic resolution knows them; they identify no
        // resource type on their own.
        .IdParameters("Payment", "paymentId")
        .IdParameters("Experience", "experienceId")
        .IdParameters("Education", "educationId")
        .IdParameters("Thread", "threadId")
        .Build();
}
