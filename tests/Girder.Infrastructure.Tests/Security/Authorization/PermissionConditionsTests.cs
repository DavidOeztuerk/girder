using Girder.Infrastructure.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Security.Authorization;

[Trait("Category", "Unit")]
public class PermissionConditionsTests
{
    private sealed record Posting(string AuthorId);

    private static PermissionConditionContext ContextFor(string userId, object resource) =>
        new(userId, "Posting", resource);

    [Fact]
    public void ADeclaredConditionIsAnswered()
    {
        var conditions = new PermissionConditionsBuilder()
            .Condition("user is the author", ctx =>
                ctx.ResourceData is Posting p && p.AuthorId == ctx.UserId)
            .Build();

        conditions.Knows("user is the author").Should().BeTrue();
        conditions.IsSatisfied("user is the author", ContextFor("u-1", new Posting("u-1")))
            .Should().BeTrue();
        conditions.IsSatisfied("user is the author", ContextFor("u-1", new Posting("u-2")))
            .Should().BeFalse();
    }

    [Fact]
    public void AnUndeclaredConditionIsDeniedAndKnownToBeUndeclared()
    {
        // Denying is right, but the caller must be able to tell "no" from
        // "never defined" — otherwise a typo looks like a working rule.
        var conditions = new PermissionConditionsBuilder()
            .Condition("user is the author", _ => true)
            .Build();

        conditions.Knows("user is teh author").Should().BeFalse();
        conditions.IsSatisfied("user is teh author", ContextFor("u-1", new Posting("u-1")))
            .Should().BeFalse();
    }

    [Fact]
    public void ConditionsAreMatchedAsWritten()
    {
        var conditions = new PermissionConditionsBuilder()
            .Condition("user is the author", _ => true)
            .Build();

        conditions.Knows("User Is The Author").Should().BeFalse();
    }

    [Fact]
    public void NoneKnowsNothing()
    {
        PermissionConditions.None.Knows("anything").Should().BeFalse();
        PermissionConditions.None.IsSatisfied("anything", ContextFor("u-1", new Posting("u-1")))
            .Should().BeFalse();
    }

    [Fact]
    public void DefiningOneConditionTwiceIsRefused()
    {
        var builder = new PermissionConditionsBuilder().Condition("same", _ => true);

        var act = () => builder.Condition("same", _ => false);

        act.Should().Throw<ArgumentException>().WithMessage("*already defined*");
    }

    [Fact]
    public void ABuiltInstanceDoesNotChangeWithTheBuilder()
    {
        var builder = new PermissionConditionsBuilder().Condition("first", _ => true);
        var conditions = builder.Build();

        builder.Condition("second", _ => true);

        conditions.Knows("second").Should().BeFalse();
    }

    [Fact]
    public void TheRegisteredInstanceIsResolvable()
    {
        var services = new ServiceCollection();
        services.AddPermissionConditions(c => c.Condition("user is the author", _ => true));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPermissionConditions>()
            .Knows("user is the author").Should().BeTrue();
    }
}
