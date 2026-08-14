using Infrastructure.Security;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class RolesTests
{
    [Fact]
    public void Roles_HasExpectedConstants()
    {
        Roles.Admin.Should().Be("Admin");
        Roles.User.Should().Be("User");
        Roles.Moderator.Should().Be("Moderator");
        Roles.SuperAdmin.Should().Be("SuperAdmin");
        Roles.Service.Should().Be("Service");
    }
}
