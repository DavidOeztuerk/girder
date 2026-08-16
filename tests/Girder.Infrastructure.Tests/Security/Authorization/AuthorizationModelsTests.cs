using Girder.Abstractions.Security.Authorization;
using Girder.Infrastructure.Security.Authorization;

namespace Girder.Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class AuthorizationModelsTests
{
    #region AuthorizationResult

    [Fact]
    public void AuthorizationResult_Success_Succeeded()
    {
        var result = AuthorizationResult.Success();

        result.Succeeded.Should().BeTrue();
        result.FailureReasons.Should().BeEmpty();
    }

    [Fact]
    public void AuthorizationResult_FailSingleReason_NotSucceeded()
    {
        var result = AuthorizationResult.Fail("Not allowed");

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().ContainSingle().Which.Should().Be("Not allowed");
    }

    [Fact]
    public void AuthorizationResult_FailMultipleReasons_AllStored()
    {
        var result = AuthorizationResult.Fail(new[] { "Reason A", "Reason B" });

        result.Succeeded.Should().BeFalse();
        result.FailureReasons.Should().HaveCount(2);
        result.FailureReasons.Should().Contain("Reason A").And.Contain("Reason B");
    }

    [Fact]
    public void AuthorizationResult_Default_ContextDictionaryInitialized()
    {
        var result = new AuthorizationResult();

        result.Context.Should().NotBeNull();
        result.FailureReasons.Should().NotBeNull();
    }

    [Fact]
    public void AuthorizationResult_Context_CanStoreArbitraryData()
    {
        var result = AuthorizationResult.Success();
        result.Context["userId"] = "u-1";
        result.Context["resource"] = "JOB";

        result.Context["userId"].Should().Be("u-1");
        result.Context["resource"].Should().Be("JOB");
    }

    #endregion

    #region PermissionGrant

    [Fact]
    public void PermissionGrant_Defaults()
    {
        var grant = new PermissionGrant();

        grant.Id.Should().NotBeNullOrEmpty(); // Guid.NewGuid()
        grant.IsActive.Should().BeTrue();
        grant.GrantedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        grant.Metadata.Should().NotBeNull();
    }

    [Fact]
    public void PermissionGrant_Setters_Work()
    {
        var expiresAt = DateTime.UtcNow.AddDays(30);
        var grant = new PermissionGrant
        {
            UserId = "u-1",
            ResourceType = "JOB",
            ResourceId = "s-1",
            Permission = "job:read",
            GrantedBy = "admin",
            ExpiresAt = expiresAt,
            IsActive = false
        };

        grant.UserId.Should().Be("u-1");
        grant.ResourceType.Should().Be("JOB");
        grant.ResourceId.Should().Be("s-1");
        grant.Permission.Should().Be("job:read");
        grant.GrantedBy.Should().Be("admin");
        grant.ExpiresAt.Should().Be(expiresAt);
        grant.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PermissionGrant_UniqueIds()
    {
        var grant1 = new PermissionGrant();
        var grant2 = new PermissionGrant();

        grant1.Id.Should().NotBe(grant2.Id);
    }

    #endregion

    #region ResourceAccess

    [Fact]
    public void ResourceAccess_Defaults()
    {
        var access = new ResourceAccess();

        access.Permissions.Should().NotBeNull();
        access.IsOwner.Should().BeFalse();
        access.ResourceType.Should().BeEmpty();
        access.ResourceId.Should().BeEmpty();
    }

    [Fact]
    public void ResourceAccess_Setters_Work()
    {
        var grantedAt = DateTime.UtcNow.AddDays(-1);
        var expiresAt = DateTime.UtcNow.AddDays(30);
        var access = new ResourceAccess
        {
            ResourceType = "USER",
            ResourceId = "user-123",
            Permissions = new List<string> { "user:read", "user:update" },
            GrantedAt = grantedAt,
            ExpiresAt = expiresAt,
            GrantedBy = "system",
            IsOwner = true
        };

        access.ResourceType.Should().Be("USER");
        access.ResourceId.Should().Be("user-123");
        access.Permissions.Should().HaveCount(2);
        access.GrantedAt.Should().Be(grantedAt);
        access.ExpiresAt.Should().Be(expiresAt);
        access.IsOwner.Should().BeTrue();
        access.GrantedBy.Should().Be("system");
    }

    #endregion

    #region ResourceAuthorizeAttribute

    [Fact]
    public void ResourceAuthorizeAttribute_DefaultConstructor()
    {
        var attr = new ResourceAuthorizeAttribute();

        attr.Resource.Should().BeNull();
        attr.Action.Should().BeNull();
        attr.RequireOwnership.Should().BeFalse();
    }

    [Fact]
    public void ResourceAuthorizeAttribute_WithResourceAndAction()
    {
        var attr = new ResourceAuthorizeAttribute("USER", "READ");

        attr.Resource.Should().Be("USER");
        attr.Action.Should().Be("READ");
    }

    [Fact]
    public void ResourceAuthorizeAttribute_RequireOwnership_Settable()
    {
        var attr = new ResourceAuthorizeAttribute { RequireOwnership = true };

        attr.RequireOwnership.Should().BeTrue();
    }

    #endregion

    #region RequireOwnershipAttribute

    [Fact]
    public void RequireOwnershipAttribute_DefaultConstructor()
    {
        var attr = new RequireOwnershipAttribute();

        attr.ResourceType.Should().BeNull();
    }

    [Fact]
    public void RequireOwnershipAttribute_WithResourceType()
    {
        var attr = new RequireOwnershipAttribute("JOB");

        attr.ResourceType.Should().Be("JOB");
    }

    #endregion

    #region SkipResourceAuthorizationAttribute

    [Fact]
    public void SkipResourceAuthorizationAttribute_CanBeInstantiated()
    {
        var attr = new SkipResourceAuthorizationAttribute();

        attr.Should().NotBeNull();
    }

    #endregion
}
