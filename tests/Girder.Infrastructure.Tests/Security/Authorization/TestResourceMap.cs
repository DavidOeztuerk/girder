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
    public const string Job = "Job";
    public const string Match = "Match";
    public const string Booking = "Booking";
    public const string Session = "Session";
    public const string Notification = "Notification";
    public const string System = "System";

    public static readonly IResourceMap Instance = new ResourceMapBuilder()
        // Route parameters that name a resource unambiguously. "id" and "userId"
        // are deliberately absent: they say nothing about which resource is meant.
        .RouteParameter(Booking, "bookingId")
        .RouteParameter(Match, "referralId", "requestId")
        .RouteParameter(Job, "jobId", "postingId", "topicId")
        .RouteParameter(Session, "sessionId")
        .RouteParameter(Notification, "notificationId", "templateId")
        .RouteParameter(System, "alertId")

        .PathSegment(User, "users", "user", "auth")
        .PathSegment(Job, "jobs", "job", "postings", "posting")
        .PathSegment(Match, "referrals", "referral", "referral-requests")
        .PathSegment(Booking, "bookings", "booking", "reviews")
        .PathSegment(Session, "session", "sessions", "calls")
        .PathSegment(Notification, "notifications", "notification", "preferences", "reminders", "templates")
        .PathSegment(System, "admin", "system")

        // Which parameter carries the id of each resource, most specific first.
        .IdParameters(Booking, "bookingId")
        .IdParameters(Match, "referralId", "requestId")
        .IdParameters(Job, "jobId", "postingId", "topicId")
        .IdParameters(User, "userId")
        .IdParameters(Session, "sessionId")
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
