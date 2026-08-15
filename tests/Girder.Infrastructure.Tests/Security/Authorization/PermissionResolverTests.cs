using Girder.Infrastructure.Security.Authorization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Security.Authorization;

/// <summary>
/// The resolver holds whatever definitions an application registers and ships
/// none of its own, so every test declares the definitions it needs.
/// </summary>
[Trait("Category", "Unit")]
public class PermissionResolverTests
{
    private const string Document = "Document";

    private readonly PermissionResolver _sut = new(NullLogger<PermissionResolver>.Instance);

    private static PermissionDefinition Definition(
        string name,
        string resourceType = Document,
        string[]? actions = null,
        bool isOwnerPermission = false,
        bool isConditional = false,
        string? minimumRole = null) =>
        new()
        {
            Name = name,
            ResourceType = resourceType,
            Actions = [.. actions ?? ["read"]],
            IsOwnerPermission = isOwnerPermission,
            IsConditional = isConditional,
            MinimumRole = minimumRole,
            Category = PermissionCategory.Standard
        };

    #region A resolver starts empty

    [Fact]
    public async Task WithoutRegistration_NothingIsRequired()
    {
        var result = await _sut.GetRequiredPermissionsAsync(Document, "read");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutRegistration_NothingIsAvailable()
    {
        var result = await _sut.GetAvailablePermissionsAsync(Document);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetRequiredPermissionsAsync

    [Fact]
    public async Task RegisteredPermission_IsRequiredForItsAction()
    {
        _sut.RegisterPermission(Definition("document:read", actions: ["read"]));

        var result = await _sut.GetRequiredPermissionsAsync(Document, "read");

        result.Should().ContainSingle().Which.Name.Should().Be("document:read");
    }

    [Fact]
    public async Task PermissionWithSeveralActions_IsRequiredForEachOfThem()
    {
        _sut.RegisterPermission(Definition("document:manage", actions: ["read", "update", "delete"]));

        foreach (var action in new[] { "read", "update", "delete" })
        {
            var result = await _sut.GetRequiredPermissionsAsync(Document, action);

            result.Should().Contain(p => p.Name == "document:manage", $"action '{action}' was declared");
        }
    }

    [Fact]
    public async Task SeveralPermissionsForOneAction_AreAllRequired()
    {
        _sut.RegisterPermission(Definition("document:read", actions: ["read"]));
        _sut.RegisterPermission(Definition("document:audit", actions: ["read"]));

        var result = await _sut.GetRequiredPermissionsAsync(Document, "read");

        result.Select(p => p.Name).Should().BeEquivalentTo(["document:read", "document:audit"]);
    }

    [Fact]
    public async Task UnknownResource_RequiresNothing()
    {
        _sut.RegisterPermission(Definition("document:read"));

        var result = await _sut.GetRequiredPermissionsAsync("Unknown", "read");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownAction_RequiresNothing()
    {
        _sut.RegisterPermission(Definition("document:read", actions: ["read"]));

        var result = await _sut.GetRequiredPermissionsAsync(Document, "incinerate");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ResourcesAreKeptApart()
    {
        _sut.RegisterPermission(Definition("document:read", Document, ["read"]));
        _sut.RegisterPermission(Definition("invoice:read", "Invoice", ["read"]));

        var documents = await _sut.GetRequiredPermissionsAsync(Document, "read");

        documents.Should().ContainSingle().Which.Name.Should().Be("document:read");
    }

    [Fact]
    public async Task ConditionAndMinimumRoleSurviveRegistration()
    {
        _sut.RegisterPermission(Definition(
            "document:sign",
            actions: ["sign"],
            isConditional: true,
            minimumRole: "Approver"));

        var result = await _sut.GetRequiredPermissionsAsync(Document, "sign");

        var permission = result.Should().ContainSingle().Subject;
        permission.IsConditional.Should().BeTrue();
        permission.MinimumRole.Should().Be("Approver");
    }

    #endregion

    #region GetOwnerPermissionsAsync

    [Fact]
    public async Task OnlyOwnerPermissionsAreReturnedForOwners()
    {
        _sut.RegisterPermission(Definition("document:read", actions: ["read"], isOwnerPermission: true));
        _sut.RegisterPermission(Definition("document:purge", actions: ["delete"], isOwnerPermission: false));

        var result = await _sut.GetOwnerPermissionsAsync(Document);

        result.Should().BeEquivalentTo(["document:read"]);
    }

    [Fact]
    public async Task UnknownResource_HasNoOwnerPermissions()
    {
        var result = await _sut.GetOwnerPermissionsAsync("Unknown");

        result.Should().BeEmpty();
    }

    #endregion

    #region GetAvailablePermissionsAsync

    [Fact]
    public async Task AvailablePermissionsListEverythingRegisteredForTheResource()
    {
        _sut.RegisterPermission(Definition("document:read", actions: ["read"]));
        _sut.RegisterPermission(Definition("document:write", actions: ["update"]));
        _sut.RegisterPermission(Definition("invoice:read", "Invoice", ["read"]));

        var result = await _sut.GetAvailablePermissionsAsync(Document);

        result.Select(p => p.Name).Should().BeEquivalentTo(["document:read", "document:write"]);
    }

    [Fact]
    public async Task UnknownResource_HasNoAvailablePermissions()
    {
        var result = await _sut.GetAvailablePermissionsAsync("Unknown");

        result.Should().BeEmpty();
    }

    #endregion

    #region RegisterPermissions

    [Fact]
    public async Task RegisterPermissions_RegistersAllOfThem()
    {
        _sut.RegisterPermissions([
            Definition("document:read", actions: ["read"]),
            Definition("document:write", actions: ["update"]),
            Definition("invoice:read", "Invoice", ["read"])
        ]);

        (await _sut.GetAvailablePermissionsAsync(Document)).Should().HaveCount(2);
        (await _sut.GetAvailablePermissionsAsync("Invoice")).Should().HaveCount(1);
    }

    #endregion
}
