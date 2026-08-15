namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Names the resource type behind a route parameter or a path segment.
/// </summary>
/// <remarks>
/// Girder knows no resource types of its own. Without a registered map nothing
/// is inferred, and endpoints must name their resource with
/// <c>[ResourceAuthorize]</c>, which fails closed.
/// </remarks>
public interface IResourceMap
{
    /// <summary>
    /// The resource type a route parameter identifies, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// Map only unambiguous names. A parameter such as <c>id</c> identifies
    /// whatever the route is about and must stay unmapped.
    /// </remarks>
    string? FromRouteParameter(string parameterName);

    /// <summary>The resource type a path segment identifies, or <c>null</c>.</summary>
    string? FromPathSegment(string segment);

    /// <summary>
    /// The route parameters that can carry the id of <paramref name="resourceType"/>,
    /// most specific first and ending with <c>id</c>. Empty when the resource type
    /// is unknown, which makes the caller fall back to a generic scan.
    /// </summary>
    IReadOnlyList<string> IdParametersFor(string resourceType);

    /// <summary>
    /// Every route parameter that can carry a resource id, used when the resource
    /// type is unknown.
    /// </summary>
    IReadOnlyCollection<string> AllIdParameters { get; }
}
