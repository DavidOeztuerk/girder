using Infrastructure.Security.Authorization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class PermissionResolverTests
{
    private readonly PermissionResolver _sut;
    private readonly ILogger<PermissionResolver> _logger = Substitute.For<ILogger<PermissionResolver>>();

    public PermissionResolverTests()
    {
        _sut = new PermissionResolver(_logger);
    }

    #region GetRequiredPermissionsAsync

    [Fact]
    public async Task GetRequiredPermissionsAsync_UserRead_ReturnsPermissions()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.USER, SkillswapActions.READ);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.USER_READ);
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_SkillCreate_ReturnsPermissions()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.SKILL, SkillswapActions.CREATE);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.SKILL_CREATE);
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_AdminAction_IncludesAdminPermission()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.USER, SkillswapActions.ADMIN);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.USER_ADMIN);
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_UnknownResource_ReturnsEmpty()
    {
        var result = await _sut.GetRequiredPermissionsAsync("UnknownResource", SkillswapActions.READ);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_UnknownAction_ReturnsEmpty()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.USER, "unknown-action");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_MatchAccept_ReturnsConditionalPermission()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.MATCH, SkillswapActions.ACCEPT);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.MATCH_ACCEPT && p.IsConditional);
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_VideocallJoin_ReturnsPermission()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.VIDEOCALL, SkillswapActions.JOIN);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.VIDEOCALL_JOIN);
    }

    [Fact]
    public async Task GetRequiredPermissionsAsync_SystemAdmin_RequiresSuperAdminRole()
    {
        var result = await _sut.GetRequiredPermissionsAsync(SkillswapResources.SYSTEM, SkillswapActions.ADMIN);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.MinimumRole == "SuperAdmin");
    }

    #endregion

    #region GetOwnerPermissionsAsync

    [Fact]
    public async Task GetOwnerPermissionsAsync_User_ReturnsOwnerPermissions()
    {
        var result = await _sut.GetOwnerPermissionsAsync(SkillswapResources.USER);

        result.Should().NotBeEmpty();
        result.Should().Contain(SkillswapPermissions.USER_READ);
        result.Should().Contain(SkillswapPermissions.USER_UPDATE);
        result.Should().Contain(SkillswapPermissions.USER_DELETE);
    }

    [Fact]
    public async Task GetOwnerPermissionsAsync_Skill_ReturnsOwnerPermissions()
    {
        var result = await _sut.GetOwnerPermissionsAsync(SkillswapResources.SKILL);

        result.Should().NotBeEmpty();
        result.Should().Contain(SkillswapPermissions.SKILL_UPDATE);
        result.Should().Contain(SkillswapPermissions.SKILL_DELETE);
    }

    [Fact]
    public async Task GetOwnerPermissionsAsync_UnknownResource_ReturnsEmpty()
    {
        var result = await _sut.GetOwnerPermissionsAsync("UnknownResource");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOwnerPermissionsAsync_Videocall_ReturnsModerateAndRecord()
    {
        var result = await _sut.GetOwnerPermissionsAsync(SkillswapResources.VIDEOCALL);

        result.Should().Contain(SkillswapPermissions.VIDEOCALL_MODERATE);
        result.Should().Contain(SkillswapPermissions.VIDEOCALL_RECORD);
    }

    #endregion

    #region GetAvailablePermissionsAsync

    [Fact]
    public async Task GetAvailablePermissionsAsync_User_ReturnsAllUserPermissions()
    {
        var result = await _sut.GetAvailablePermissionsAsync(SkillswapResources.USER);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.USER_READ);
        result.Should().Contain(p => p.Name == SkillswapPermissions.USER_UPDATE);
        result.Should().Contain(p => p.Name == SkillswapPermissions.USER_DELETE);
        result.Should().Contain(p => p.Name == SkillswapPermissions.USER_ADMIN);
    }

    [Fact]
    public async Task GetAvailablePermissionsAsync_Match_ReturnsAllMatchPermissions()
    {
        var result = await _sut.GetAvailablePermissionsAsync(SkillswapResources.MATCH);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.MATCH_READ);
        result.Should().Contain(p => p.Name == SkillswapPermissions.MATCH_CREATE);
        result.Should().Contain(p => p.Name == SkillswapPermissions.MATCH_ACCEPT);
        result.Should().Contain(p => p.Name == SkillswapPermissions.MATCH_REJECT);
    }

    [Fact]
    public async Task GetAvailablePermissionsAsync_UnknownResource_ReturnsEmpty()
    {
        var result = await _sut.GetAvailablePermissionsAsync("Unknown");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailablePermissionsAsync_Appointment_ReturnsAllAppointmentPermissions()
    {
        var result = await _sut.GetAvailablePermissionsAsync(SkillswapResources.APPOINTMENT);

        result.Should().NotBeEmpty();
        result.Should().Contain(p => p.Name == SkillswapPermissions.APPOINTMENT_CREATE);
        result.Should().Contain(p => p.Name == SkillswapPermissions.APPOINTMENT_JOIN);
    }

    [Fact]
    public async Task GetAvailablePermissionsAsync_Notification_ReturnsNotificationPermissions()
    {
        var result = await _sut.GetAvailablePermissionsAsync(SkillswapResources.NOTIFICATION);

        result.Should().Contain(p => p.Name == SkillswapPermissions.NOTIFICATION_SEND);
        result.Should().Contain(p => p.Name == SkillswapPermissions.NOTIFICATION_ADMIN);
    }

    #endregion

    #region RegisterPermission

    [Fact]
    public async Task RegisterPermission_NewPermission_IsAvailable()
    {
        var permission = new PermissionDefinition
        {
            Name = "custom:read",
            ResourceType = "CustomResource",
            Actions = new List<string> { "read" },
            Category = PermissionCategory.Standard
        };

        _sut.RegisterPermission(permission);

        var result = await _sut.GetAvailablePermissionsAsync("CustomResource");
        result.Should().Contain(p => p.Name == "custom:read");
    }

    [Fact]
    public async Task RegisterPermission_WithMultipleActions_RegisteredForAllActions()
    {
        var permission = new PermissionDefinition
        {
            Name = "custom:full",
            ResourceType = "CustomResource",
            Actions = new List<string> { "read", "write", "delete" },
            Category = PermissionCategory.Administrative
        };

        _sut.RegisterPermission(permission);

        var readPerms = await _sut.GetRequiredPermissionsAsync("CustomResource", "read");
        var writePerms = await _sut.GetRequiredPermissionsAsync("CustomResource", "write");
        var deletePerms = await _sut.GetRequiredPermissionsAsync("CustomResource", "delete");

        readPerms.Should().Contain(p => p.Name == "custom:full");
        writePerms.Should().Contain(p => p.Name == "custom:full");
        deletePerms.Should().Contain(p => p.Name == "custom:full");
    }

    #endregion

    #region RegisterPermissions

    [Fact]
    public async Task RegisterPermissions_MultiplePermissions_AllRegistered()
    {
        var permissions = new[]
        {
            new PermissionDefinition
            {
                Name = "batch:read",
                ResourceType = "BatchResource",
                Actions = new List<string> { "read" }
            },
            new PermissionDefinition
            {
                Name = "batch:write",
                ResourceType = "BatchResource",
                Actions = new List<string> { "write" }
            }
        };

        _sut.RegisterPermissions(permissions);

        var result = await _sut.GetAvailablePermissionsAsync("BatchResource");
        result.Should().HaveCount(2);
    }

    #endregion

    #region InitializeSkillswapPermissions (integration check)

    [Fact]
    public async Task Constructor_InitializesAllResourceTypes()
    {
        var resources = new[]
        {
            SkillswapResources.USER,
            SkillswapResources.SKILL,
            SkillswapResources.MATCH,
            SkillswapResources.APPOINTMENT,
            SkillswapResources.VIDEOCALL,
            SkillswapResources.SYSTEM,
            SkillswapResources.NOTIFICATION
        };

        foreach (var resource in resources)
        {
            var permissions = await _sut.GetAvailablePermissionsAsync(resource);
            permissions.Should().NotBeEmpty($"Resource {resource} should have permissions");
        }
    }

    #endregion
}
