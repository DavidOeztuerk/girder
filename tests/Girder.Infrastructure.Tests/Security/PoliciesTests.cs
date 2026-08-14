using Girder.Infrastructure.Security;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class PoliciesTests
{
    [Fact]
    public void Policies_HasExpectedConstants()
    {
        Policies.RequireAdminRole.Should().Be("RequireAdminRole");
        Policies.RequireModeratorRole.Should().Be("RequireModeratorRole");
        Policies.RequireUserRole.Should().Be("RequireUserRole");
        Policies.RequireVerifiedEmail.Should().Be("RequireVerifiedEmail");
        Policies.RequireActiveAccount.Should().Be("RequireActiveAccount");
        Policies.CanManageUsers.Should().Be("CanManageUsers");
        Policies.CanManageSkills.Should().Be("CanManageSkills");
        Policies.CanViewSystemLogs.Should().Be("CanViewSystemLogs");
    }
}
