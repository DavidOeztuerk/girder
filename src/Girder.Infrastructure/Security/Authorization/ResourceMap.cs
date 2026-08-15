using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// An immutable <see cref="IResourceMap"/>. Build one with
/// <see cref="ResourceMapBuilder"/>.
/// </summary>
public sealed class ResourceMap : IResourceMap
{
    /// <summary>
    /// The universal fallback parameter name, appended to every resource's id
    /// parameters and always part of <see cref="AllIdParameters"/>.
    /// </summary>
    public const string GenericIdParameter = "id";

    private readonly IReadOnlyDictionary<string, string> _routeParameters;
    private readonly IReadOnlyDictionary<string, string> _pathSegments;
    private readonly IReadOnlyDictionary<string, string[]> _idParameters;

    internal ResourceMap(
        IReadOnlyDictionary<string, string> routeParameters,
        IReadOnlyDictionary<string, string> pathSegments,
        IReadOnlyDictionary<string, string[]> idParameters)
    {
        _routeParameters = routeParameters;
        _pathSegments = pathSegments;
        _idParameters = idParameters;

        AllIdParameters = idParameters.Values
            .SelectMany(p => p)
            .Append(GenericIdParameter)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>A map that identifies nothing, so every resource must be named explicitly.</summary>
    public static IResourceMap Empty { get; } = new ResourceMap(
        new Dictionary<string, string>(),
        new Dictionary<string, string>(),
        new Dictionary<string, string[]>());

    /// <inheritdoc />
    public IReadOnlyCollection<string> AllIdParameters { get; }

    /// <inheritdoc />
    public string? FromRouteParameter(string parameterName) =>
        _routeParameters.TryGetValue(parameterName, out var type) ? type : null;

    /// <inheritdoc />
    public string? FromPathSegment(string segment) =>
        _pathSegments.TryGetValue(segment, out var type) ? type : null;

    /// <inheritdoc />
    public IReadOnlyList<string> IdParametersFor(string resourceType) =>
        _idParameters.TryGetValue(resourceType, out var parameters) ? parameters : [];
}

/// <summary>
/// Declares which route parameters and path segments identify which resources.
/// </summary>
/// <example>
/// <code>
/// services.AddResourceMap(m => m
///     .RouteParameter("Job", "jobId", "postingId")
///     .PathSegment("Job", "jobs", "postings")
///     .PathSegment("Company", "companies")
///     .IdParameters("Job", "jobId", "postingId"));
/// </code>
/// </example>
public sealed class ResourceMapBuilder
{
    // Route parameters are compared as written, path segments case-insensitively,
    // matching how each appears in a request.
    private readonly Dictionary<string, string> _routeParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pathSegments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _idParameters = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps route parameter names onto <paramref name="resourceType"/>.</summary>
    public ResourceMapBuilder RouteParameter(string resourceType, params string[] parameterNames) =>
        Add(_routeParameters, resourceType, parameterNames, nameof(parameterNames));

    /// <summary>Maps path segments onto <paramref name="resourceType"/>.</summary>
    public ResourceMapBuilder PathSegment(string resourceType, params string[] segments) =>
        Add(_pathSegments, resourceType, segments, nameof(segments));

    /// <summary>
    /// Declares which route parameters carry the id of
    /// <paramref name="resourceType"/>, most specific first.
    /// </summary>
    /// <remarks>
    /// <c>id</c> is appended automatically as the last resort, so it need not be
    /// listed.
    /// </remarks>
    public ResourceMapBuilder IdParameters(string resourceType, params string[] parameterNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentNullException.ThrowIfNull(parameterNames);

        var ordered = _idParameters.TryGetValue(resourceType, out var existing)
            ? existing
            : _idParameters[resourceType] = [];

        foreach (var name in parameterNames)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            if (!ordered.Contains(name, StringComparer.Ordinal))
            {
                ordered.Add(name);
            }
        }

        return this;
    }

    public IResourceMap Build() =>
        new ResourceMap(
            new Dictionary<string, string>(_routeParameters, StringComparer.Ordinal),
            new Dictionary<string, string>(_pathSegments, StringComparer.OrdinalIgnoreCase),
            _idParameters.ToDictionary(
                e => e.Key,
                e => e.Value
                    .Append(ResourceMap.GenericIdParameter)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase));

    private ResourceMapBuilder Add(
        Dictionary<string, string> target,
        string resourceType,
        string[] keys,
        string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentNullException.ThrowIfNull(keys);

        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            if (target.TryGetValue(key, out var existing)
                && !string.Equals(existing, resourceType, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"'{key}' is already mapped to '{existing}' and cannot also mean "
                    + $"'{resourceType}'. One name cannot identify two resources.",
                    parameterName);
            }

            target[key] = resourceType;
        }

        return this;
    }
}

public static class ResourceMapExtensions
{
    /// <summary>Registers the application's resource map.</summary>
    public static IServiceCollection AddResourceMap(
        this IServiceCollection services,
        Action<ResourceMapBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new ResourceMapBuilder();
        configure(builder);

        return services.AddSingleton(builder.Build());
    }
}
