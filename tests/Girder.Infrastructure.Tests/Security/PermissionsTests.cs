using Infrastructure.Security;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class PermissionsTests
{
    [Theory]
    [InlineData(nameof(Permissions.SystemManageAll), "system:manage_all")]
    [InlineData(nameof(Permissions.UsersCreate), "users:create")]
    [InlineData(nameof(Permissions.ProfileViewOwn), "profile:view_own")]
    [InlineData(nameof(Permissions.SkillsCreateOwn), "skills:create_own")]
    [InlineData(nameof(Permissions.AppointmentsCreate), "appointments:create")]
    [InlineData(nameof(Permissions.MatchingAccess), "matching:access")]
    [InlineData(nameof(Permissions.MessagesSend), "messages:send")]
    [InlineData(nameof(Permissions.VideoCallsAccess), "videocalls:access")]
    [InlineData(nameof(Permissions.ContentModerate), "content:moderate")]
    [InlineData(nameof(Permissions.AdminAccessDashboard), "admin:access_dashboard")]
    [InlineData(nameof(Permissions.SecurityViewAlerts), "security:view_alerts")]
    [InlineData(nameof(Permissions.RolesCreate), "roles:create")]
    public void Permission_HasExpectedValue(string fieldName, string expectedValue)
    {
        var field = typeof(Permissions).GetField(fieldName);
        field.Should().NotBeNull();
        field!.GetValue(null).Should().Be(expectedValue);
    }

    [Fact]
    public void Permissions_AllConstantsFollowNamingConvention()
    {
        var fields = typeof(Permissions).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        fields.Should().NotBeEmpty();
        foreach (var field in fields)
        {
            var value = (string)field.GetValue(null)!;
            value.Should().Contain(":", $"Permission '{field.Name}' should follow 'category:action' format");
        }
    }
}
