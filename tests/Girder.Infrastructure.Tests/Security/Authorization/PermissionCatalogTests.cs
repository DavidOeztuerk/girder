using Girder.Infrastructure.Security.Authorization;

namespace Girder.Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class PermissionCatalogTests
{
    // A four-level hierarchy, so inheritance is tested transitively rather than
    // one step deep.
    private static IPermissionCatalog Catalog() => new PermissionCatalogBuilder()
        .Role("User", "profile:read_own", "profile:update_own")
        .Role("Moderator", "content:moderate")
        .Role("Admin", "users:view_all", "users:block")
        .Role("SuperAdmin", "system:manage_all")
        .RoleInherits("Moderator", "User")
        .RoleInherits("Admin", "Moderator")
        .RoleInherits("SuperAdmin", "Admin")
        .Build();

    [Fact]
    public void RoleGetsItsOwnPermissions()
    {
        Catalog().PermissionsFor(["User"])
            .Should().BeEquivalentTo(["profile:read_own", "profile:update_own"]);
    }

    [Fact]
    public void RoleDoesNotGetPermissionsOfARoleAboveIt()
    {
        Catalog().PermissionsFor(["User"]).Should().NotContain("users:view_all");
    }

    [Fact]
    public void InheritedPermissionsAreIncluded()
    {
        var permissions = Catalog().PermissionsFor(["Moderator"]);

        permissions.Should().Contain("content:moderate");
        permissions.Should().Contain("profile:read_own");
    }

    [Fact]
    public void InheritanceIsTransitive()
    {
        var permissions = Catalog().PermissionsFor(["SuperAdmin"]);

        permissions.Should().Contain("system:manage_all");
        permissions.Should().Contain("users:view_all");
        permissions.Should().Contain("content:moderate");
        permissions.Should().Contain("profile:read_own");
    }

    [Fact]
    public void SeveralRolesCombine()
    {
        var permissions = Catalog().PermissionsFor(["User", "Moderator"]);

        permissions.Should().Contain("profile:read_own");
        permissions.Should().Contain("content:moderate");
    }

    [Fact]
    public void ResultContainsNoDuplicates()
    {
        var permissions = Catalog().PermissionsFor(["Admin", "Moderator", "User"]);

        permissions.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void UnknownRoleGrantsNothing()
    {
        Catalog().PermissionsFor(["Nonexistent"]).Should().BeEmpty();
    }

    [Fact]
    public void NoRolesGrantNothing()
    {
        Catalog().PermissionsFor([]).Should().BeEmpty();
    }

    [Fact]
    public void DirectPermissionsExcludeInheritedOnes()
    {
        var direct = Catalog().DirectPermissionsFor("Moderator");

        direct.Should().BeEquivalentTo(["content:moderate"]);
        direct.Should().NotContain("profile:read_own");
    }

    [Fact]
    public void DirectPermissionsForAnUnknownRoleAreEmpty()
    {
        Catalog().DirectPermissionsFor("Nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void RoleGrantsCoversDirectAndInherited()
    {
        var catalog = Catalog();

        catalog.RoleGrants("Admin", "users:view_all").Should().BeTrue();
        catalog.RoleGrants("Admin", "profile:read_own").Should().BeTrue("through Moderator and User");
        catalog.RoleGrants("User", "system:manage_all").Should().BeFalse();
    }

    [Fact]
    public void RolesInheritedByExcludesTheRoleItself()
    {
        var inherited = Catalog().RolesInheritedBy("Admin");

        inherited.Should().BeEquivalentTo(["Moderator", "User"]);
        inherited.Should().NotContain("Admin");
    }

    [Fact]
    public void RolesInheritedByALeafRoleIsEmpty()
    {
        Catalog().RolesInheritedBy("User").Should().BeEmpty();
    }

    [Fact]
    public void AllPermissionsListsEveryDeclaredPermission()
    {
        Catalog().AllPermissions.Should().BeEquivalentTo([
            "profile:read_own", "profile:update_own", "content:moderate",
            "users:view_all", "users:block", "system:manage_all"
        ]);
    }

    [Fact]
    public void EmptyCatalogGrantsNothing()
    {
        PermissionCatalog.Empty.PermissionsFor(["Admin"]).Should().BeEmpty();
        PermissionCatalog.Empty.AllPermissions.Should().BeEmpty();
        PermissionCatalog.Empty.RoleGrants("Admin", "anything").Should().BeFalse();
    }

    [Fact]
    public void RepeatedRoleCallsAccumulate()
    {
        var catalog = new PermissionCatalogBuilder()
            .Role("User", "a")
            .Role("User", "b")
            .Build();

        catalog.DirectPermissionsFor("User").Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void CyclicInheritanceTerminates()
    {
        // Not a supported configuration, but it must not hang.
        var catalog = new PermissionCatalogBuilder()
            .Role("A", "a")
            .Role("B", "b")
            .RoleInherits("A", "B")
            .RoleInherits("B", "A")
            .Build();

        catalog.PermissionsFor(["A"]).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void RoleCannotInheritFromItself()
    {
        var act = () => new PermissionCatalogBuilder().RoleInherits("A", "A");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BlankRolesAndPermissionsAreRejected()
    {
        var blankRole = () => new PermissionCatalogBuilder().Role("  ", "a");
        var blankPermission = () => new PermissionCatalogBuilder().Role("A", "  ");

        blankRole.Should().Throw<ArgumentException>();
        blankPermission.Should().Throw<ArgumentException>();
    }
}
