using Girder.Infrastructure.Security;

namespace Girder.Infrastructure.Tests.Security;

public class RolePermissionsTests
{
    [Fact]
    public void User_ShouldHaveBasicPermissions()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.User]).ToList();

        perms.Should().Contain(Permissions.ProfileViewOwn);
        perms.Should().Contain(Permissions.ProfileUpdateOwn);
        perms.Should().Contain(Permissions.SkillsCreateOwn);
        perms.Should().Contain(Permissions.AppointmentsCreate);
        perms.Should().Contain(Permissions.MatchingAccess);
        perms.Should().Contain(Permissions.MessagesSend);
        perms.Should().Contain(Permissions.VideoCallsAccess);
    }

    [Fact]
    public void User_ShouldNotHaveAdminPermissions()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.User]).ToList();

        perms.Should().NotContain(Permissions.UsersViewAll);
        perms.Should().NotContain(Permissions.AdminAccessDashboard);
        perms.Should().NotContain(Permissions.SystemManageAll);
    }

    [Fact]
    public void Moderator_ShouldInheritUserPermissions()
    {
        var modPerms = RolePermissions.GetPermissionsForRoles([Roles.Moderator]).ToList();
        var userPerms = RolePermissions.GetPermissionsForRoles([Roles.User]).ToList();

        foreach (var perm in userPerms)
        {
            modPerms.Should().Contain(perm,
                $"Moderator should inherit User permission '{perm}'");
        }
    }

    [Fact]
    public void Moderator_ShouldHaveContentModeration()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.Moderator]).ToList();

        perms.Should().Contain(Permissions.ContentModerate);
        perms.Should().Contain(Permissions.ReportsHandle);
        perms.Should().Contain(Permissions.SkillsVerify);
        perms.Should().Contain(Permissions.ReviewsModerate);
        perms.Should().Contain(Permissions.ModeratorAccessPanel);
    }

    [Fact]
    public void Admin_ShouldInheritModeratorAndUserPermissions()
    {
        var adminPerms = RolePermissions.GetPermissionsForRoles([Roles.Admin]).ToList();
        var modPerms = RolePermissions.GetPermissionsForRoles([Roles.Moderator]).ToList();

        foreach (var perm in modPerms)
        {
            adminPerms.Should().Contain(perm,
                $"Admin should inherit Moderator permission '{perm}'");
        }
    }

    [Fact]
    public void Admin_ShouldHaveManagementPermissions()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.Admin]).ToList();

        perms.Should().Contain(Permissions.UsersViewAll);
        perms.Should().Contain(Permissions.UsersBlock);
        perms.Should().Contain(Permissions.AdminAccessDashboard);
        perms.Should().Contain(Permissions.SecurityViewAlerts);
    }

    [Fact]
    public void Admin_ShouldNotHaveSuperAdminPermissions()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.Admin]).ToList();

        perms.Should().NotContain(Permissions.SystemManageAll);
        perms.Should().NotContain(Permissions.UsersDelete);
        perms.Should().NotContain(Permissions.UsersManageRoles);
        perms.Should().NotContain(Permissions.RolesCreate);
    }

    [Fact]
    public void SuperAdmin_ShouldInheritAllRolePermissions()
    {
        var superAdminPerms = RolePermissions.GetPermissionsForRoles([Roles.SuperAdmin]).ToList();
        var adminPerms = RolePermissions.GetPermissionsForRoles([Roles.Admin]).ToList();

        foreach (var perm in adminPerms)
        {
            superAdminPerms.Should().Contain(perm,
                $"SuperAdmin should inherit Admin permission '{perm}'");
        }
    }

    [Fact]
    public void SuperAdmin_ShouldHaveSystemManageAll()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.SuperAdmin]).ToList();
        perms.Should().Contain(Permissions.SystemManageAll);
    }

    [Fact]
    public void SuperAdmin_ShouldHaveRoleManagement()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.SuperAdmin]).ToList();

        perms.Should().Contain(Permissions.RolesCreate);
        perms.Should().Contain(Permissions.RolesUpdate);
        perms.Should().Contain(Permissions.RolesDelete);
        perms.Should().Contain(Permissions.PermissionsManage);
    }

    [Fact]
    public void MultipleRoles_ShouldCombinePermissions()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.User, Roles.Moderator]).ToList();
        var modPerms = RolePermissions.GetPermissionsForRoles([Roles.Moderator]).ToList();

        perms.Should().BeEquivalentTo(modPerms,
            "User + Moderator should equal Moderator (since Moderator inherits User)");
    }

    [Fact]
    public void UnknownRole_ShouldReturnEmpty()
    {
        var perms = RolePermissions.GetPermissionsForRoles(["UnknownRole"]).ToList();
        perms.Should().BeEmpty();
    }

    [Fact]
    public void EmptyRoles_ShouldReturnEmpty()
    {
        var perms = RolePermissions.GetPermissionsForRoles([]).ToList();
        perms.Should().BeEmpty();
    }

    [Fact]
    public void GetDirectPermissionsForRole_User_ShouldNotIncludeInherited()
    {
        var directPerms = RolePermissions.GetDirectPermissionsForRole(Roles.User).ToList();

        directPerms.Should().Contain(Permissions.ProfileViewOwn);
        directPerms.Should().Contain(Permissions.SkillsCreateOwn);
    }

    [Fact]
    public void GetDirectPermissionsForRole_Moderator_ShouldNotIncludeUserPerms()
    {
        var directPerms = RolePermissions.GetDirectPermissionsForRole(Roles.Moderator).ToList();

        // Direct moderator permissions
        directPerms.Should().Contain(Permissions.ContentModerate);

        // Should NOT contain inherited User permissions
        directPerms.Should().NotContain(Permissions.ProfileViewOwn);
        directPerms.Should().NotContain(Permissions.SkillsCreateOwn);
    }

    [Fact]
    public void GetDirectPermissionsForRole_UnknownRole_ShouldReturnEmpty()
    {
        var perms = RolePermissions.GetDirectPermissionsForRole("Unknown").ToList();
        perms.Should().BeEmpty();
    }

    [Fact]
    public void RoleHasPermission_AdminHasUsersViewAll_ShouldBeTrue()
    {
        RolePermissions.RoleHasPermission(Roles.Admin, Permissions.UsersViewAll)
            .Should().BeTrue();
    }

    [Fact]
    public void RoleHasPermission_AdminHasProfileViewOwn_ShouldBeTrue_ViaInheritance()
    {
        // Admin inherits Moderator inherits User, User has ProfileViewOwn
        RolePermissions.RoleHasPermission(Roles.Admin, Permissions.ProfileViewOwn)
            .Should().BeTrue();
    }

    [Fact]
    public void RoleHasPermission_UserHasSystemManageAll_ShouldBeFalse()
    {
        RolePermissions.RoleHasPermission(Roles.User, Permissions.SystemManageAll)
            .Should().BeFalse();
    }

    [Fact]
    public void Permissions_ShouldReturnDistinctValues()
    {
        var perms = RolePermissions.GetPermissionsForRoles([Roles.SuperAdmin]).ToList();
        perms.Should().OnlyHaveUniqueItems();
    }
}
