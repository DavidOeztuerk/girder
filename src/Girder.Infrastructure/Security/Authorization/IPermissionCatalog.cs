namespace Girder.Infrastructure.Security.Authorization;

/// <summary>
/// The set of permissions an application defines, and which roles grant them.
/// </summary>
/// <remarks>
/// Girder ships no permissions of its own. Supply a catalogue with
/// <c>AddPermissionCatalog</c>; without one, no role grants anything and only
/// explicit permission claims count.
/// </remarks>
public interface IPermissionCatalog
{
    /// <summary>Every permission the application knows about.</summary>
    IReadOnlyCollection<string> AllPermissions { get; }

    /// <summary>
    /// The permissions granted by <paramref name="roles"/>, including those
    /// granted through inheritance.
    /// </summary>
    IReadOnlyCollection<string> PermissionsFor(IEnumerable<string> roles);

    /// <summary>
    /// The permissions granted by <paramref name="role"/> alone, ignoring
    /// inheritance.
    /// </summary>
    IReadOnlyCollection<string> DirectPermissionsFor(string role);

    /// <summary>
    /// The roles <paramref name="role"/> inherits, transitively and excluding
    /// itself.
    /// </summary>
    IReadOnlyCollection<string> RolesInheritedBy(string role);

    /// <summary>
    /// Whether <paramref name="role"/> grants <paramref name="permission"/>,
    /// directly or through inheritance.
    /// </summary>
    bool RoleGrants(string role, string permission);
}
