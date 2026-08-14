namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// An immutable <see cref="IPermissionCatalog"/>. Build one with
/// <see cref="PermissionCatalogBuilder"/>.
/// </summary>
public sealed class PermissionCatalog : IPermissionCatalog
{
    private readonly IReadOnlyDictionary<string, string[]> _rolePermissions;
    private readonly IReadOnlyDictionary<string, string[]> _roleInheritance;

    internal PermissionCatalog(
        IReadOnlyDictionary<string, string[]> rolePermissions,
        IReadOnlyDictionary<string, string[]> roleInheritance)
    {
        _rolePermissions = rolePermissions;
        _roleInheritance = roleInheritance;

        AllPermissions = rolePermissions.Values
            .SelectMany(p => p)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Held by a caller who may do anything. Recognised in addition to the
    /// <c>area:*</c> form, where <c>users:*</c> satisfies <c>users:create</c>.
    /// </summary>
    public const string Wildcard = "*";

    /// <summary>A catalogue in which no role grants any permission.</summary>
    public static IPermissionCatalog Empty { get; } = new PermissionCatalog(
        new Dictionary<string, string[]>(),
        new Dictionary<string, string[]>());

    /// <inheritdoc />
    public IReadOnlyCollection<string> AllPermissions { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> PermissionsFor(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            Expand(role, expanded);
        }

        return expanded
            .SelectMany(DirectPermissionsFor)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> DirectPermissionsFor(string role) =>
        _rolePermissions.TryGetValue(role, out var permissions) ? permissions : [];

    /// <inheritdoc />
    public IReadOnlyCollection<string> RolesInheritedBy(string role)
    {
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        Expand(role, expanded);
        expanded.Remove(role);

        return expanded.ToArray();
    }

    /// <inheritdoc />
    public bool RoleGrants(string role, string permission) =>
        PermissionsFor([role]).Contains(permission, StringComparer.Ordinal);

    private void Expand(string role, HashSet<string> collected)
    {
        if (!collected.Add(role))
        {
            // Already seen; also stops a cycle from recursing forever.
            return;
        }

        if (_roleInheritance.TryGetValue(role, out var inherited))
        {
            foreach (var parent in inherited)
            {
                Expand(parent, collected);
            }
        }
    }
}
