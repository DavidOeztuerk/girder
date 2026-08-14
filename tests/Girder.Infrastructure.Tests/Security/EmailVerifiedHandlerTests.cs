using System.Security.Claims;
using Girder.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class EmailVerifiedHandlerTests
{
    private readonly EmailVerifiedHandler _handler;

    public EmailVerifiedHandlerTests()
    {
        var logger = Substitute.For<ILogger<EmailVerifiedHandler>>();
        _handler = new EmailVerifiedHandler(logger);
    }

    private static AuthorizationHandlerContext CreateContext(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var requirement = new EmailVerifiedRequirement();
        return new AuthorizationHandlerContext(
            [requirement], principal, null);
    }

    [Fact]
    public async Task HandleRequirementAsync_EmailVerifiedTrue_Succeeds()
    {
        var context = CreateContext(
            new Claim("email_verified", "True"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_EmailVerifiedFalse_Fails()
    {
        var context = CreateContext(
            new Claim("email_verified", "False"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoClaim_Fails()
    {
        var context = CreateContext(
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_InvalidBooleanValue_Fails()
    {
        var context = CreateContext(
            new Claim("email_verified", "not-a-bool"),
            new Claim(ClaimTypes.NameIdentifier, "user-1"));

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
    }
}
