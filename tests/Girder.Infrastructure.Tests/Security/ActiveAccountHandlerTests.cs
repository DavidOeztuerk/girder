using System.Security.Claims;
using Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class ActiveAccountHandlerTests
{
    private readonly ActiveAccountHandler _handler;

    public ActiveAccountHandlerTests()
    {
        var logger = Substitute.For<ILogger<ActiveAccountHandler>>();
        _handler = new ActiveAccountHandler(logger);
    }

    private static AuthorizationHandlerContext CreateContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var requirement = new ActiveAccountRequirement();
        return new AuthorizationHandlerContext(
            [requirement], principal, null);
    }

    [Fact]
    public async Task HandleRequirementAsync_ActiveAccount_Succeeds()
    {
        var context = CreateContext(
            new Claim("account_status", "Active"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_ActiveAccountCaseInsensitive_Succeeds()
    {
        var context = CreateContext(
            new Claim("account_status", "active"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_SuspendedAccount_Fails()
    {
        var context = CreateContext(
            new Claim("account_status", "Suspended"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoAccountStatusClaim_Fails()
    {
        var context = CreateContext(
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoUserIdClaim_StillFails()
    {
        // No account_status, no NameIdentifier - logs "Unknown"
        var context = CreateContext();

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }
}
