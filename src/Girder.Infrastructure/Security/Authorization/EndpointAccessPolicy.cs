using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// An <see cref="IEndpointAccessPolicy"/> made of ordered rules. The first
/// matching rule wins.
/// </summary>
public sealed class EndpointAccessPolicy : IEndpointAccessPolicy
{
    private readonly IReadOnlyList<Func<HttpContext, bool>> _public;
    private readonly IReadOnlyList<(Func<HttpContext, bool> Matches, string Permission)> _required;

    internal EndpointAccessPolicy(
        IReadOnlyList<Func<HttpContext, bool>> publicRules,
        IReadOnlyList<(Func<HttpContext, bool>, string)> requiredRules)
    {
        _public = publicRules;
        _required = requiredRules;
    }

    /// <summary>
    /// A policy with no rules: nothing is public by path and no permission is
    /// required by path. Endpoint attributes still apply.
    /// </summary>
    public static IEndpointAccessPolicy Empty { get; } = new EndpointAccessPolicy([], []);

    /// <inheritdoc />
    public bool IsPublic(HttpContext context)
    {
        foreach (var rule in _public)
        {
            if (rule(context))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public string? RequiredPermission(HttpContext context)
    {
        foreach (var (matches, permission) in _required)
        {
            if (matches(context))
            {
                return permission;
            }
        }

        return null;
    }
}

/// <summary>
/// Declares which paths are public and which permissions paths require.
/// </summary>
/// <example>
/// <code>
/// services.AddEndpointAccessPolicy(p => p
///     .Public("/health", "/swagger")
///     .Require("users:view_all", "/admin/users", "GET")
///     .Require("users:delete", "/admin/users", "DELETE"));
/// </code>
/// </example>
public sealed class EndpointAccessPolicyBuilder
{
    private readonly List<Func<HttpContext, bool>> _public = [];
    private readonly List<(Func<HttpContext, bool>, string)> _required = [];

    /// <summary>Marks path prefixes as reachable without authentication.</summary>
    public EndpointAccessPolicyBuilder Public(params string[] pathPrefixes)
    {
        ArgumentNullException.ThrowIfNull(pathPrefixes);

        foreach (var prefix in pathPrefixes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
            var segment = new PathString(prefix);
            _public.Add(c => c.Request.Path.StartsWithSegments(segment, StringComparison.OrdinalIgnoreCase));
        }

        return this;
    }

    /// <summary>Marks requests matching <paramref name="predicate"/> as public.</summary>
    public EndpointAccessPolicyBuilder Public(Func<HttpContext, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        _public.Add(predicate);

        return this;
    }

    /// <summary>
    /// Requires <paramref name="permission"/> below <paramref name="pathPrefix"/>,
    /// optionally only for the given HTTP methods.
    /// </summary>
    public EndpointAccessPolicyBuilder Require(
        string permission,
        string pathPrefix,
        params string[] methods)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentException.ThrowIfNullOrWhiteSpace(pathPrefix);

        var segment = new PathString(pathPrefix);
        var allowed = methods is { Length: > 0 } ? methods : null;

        _required.Add((c =>
            c.Request.Path.StartsWithSegments(segment, StringComparison.OrdinalIgnoreCase)
            && (allowed is null
                || allowed.Contains(c.Request.Method, StringComparer.OrdinalIgnoreCase)),
            permission));

        return this;
    }

    /// <summary>
    /// Requires <paramref name="permission"/> for requests matching
    /// <paramref name="predicate"/>.
    /// </summary>
    public EndpointAccessPolicyBuilder Require(string permission, Func<HttpContext, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        ArgumentNullException.ThrowIfNull(predicate);

        _required.Add((predicate, permission));

        return this;
    }

    public IEndpointAccessPolicy Build() => new EndpointAccessPolicy(_public, _required);
}

public static class EndpointAccessPolicyExtensions
{
    /// <summary>Registers the application's endpoint access policy.</summary>
    public static IServiceCollection AddEndpointAccessPolicy(
        this IServiceCollection services,
        Action<EndpointAccessPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new EndpointAccessPolicyBuilder();
        configure(builder);

        return services.AddSingleton(builder.Build());
    }
}
