using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// Declares which roles grant which permissions, and which roles inherit others.
/// </summary>
/// <example>
/// <code>
/// services.AddPermissionCatalog(c => c
///     .Role("User", "profile:read_own", "profile:update_own")
///     .Role("Admin", "users:manage")
///     .RoleInherits("Admin", "User"));
/// </code>
/// </example>
public sealed class PermissionCatalogBuilder
{
    private readonly Dictionary<string, HashSet<string>> _permissions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _inheritance = new(StringComparer.Ordinal);

    /// <summary>Grants <paramref name="permissions"/> to <paramref name="role"/>.</summary>
    /// <remarks>Calling this repeatedly for the same role adds to it.</remarks>
    public PermissionCatalogBuilder Role(string role, params string[] permissions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(permissions);

        var set = _permissions.TryGetValue(role, out var existing)
            ? existing
            : _permissions[role] = new HashSet<string>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(permission);
            set.Add(permission);
        }

        return this;
    }

    /// <summary>
    /// Makes <paramref name="role"/> inherit everything granted by
    /// <paramref name="inheritedRoles"/>.
    /// </summary>
    public PermissionCatalogBuilder RoleInherits(string role, params string[] inheritedRoles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(inheritedRoles);

        var set = _inheritance.TryGetValue(role, out var existing)
            ? existing
            : _inheritance[role] = new HashSet<string>(StringComparer.Ordinal);

        foreach (var inherited in inheritedRoles)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(inherited);

            if (string.Equals(inherited, role, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Role '{role}' cannot inherit from itself.", nameof(inheritedRoles));
            }

            set.Add(inherited);
        }

        return this;
    }

    public IPermissionCatalog Build() =>
        new PermissionCatalog(
            _permissions.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal),
            _inheritance.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal));
}

public static class PermissionCatalogExtensions
{
    /// <summary>
    /// Registers the application's permission catalogue.
    /// </summary>
    public static IServiceCollection AddPermissionCatalog(
        this IServiceCollection services,
        Action<PermissionCatalogBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PermissionCatalogBuilder();
        configure(builder);

        return services.AddSingleton(builder.Build());
    }
}
