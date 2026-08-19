using Girder.Abstractions.Security;
using Girder.InMemory.Security;
using Girder.Redis.Security;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// The revocation stores must be registerable, and the read and the write side
/// must be the same object.
/// </summary>
/// <remarks>
/// Two instances would compile, pass a naive test and revoke nothing: the
/// writer records into its own state and the evaluator reads an empty one.
/// </remarks>
[Trait("Category", "Unit")]
public class TokenRevocationRegistrationTests
{
    [Fact]
    public void The_in_memory_store_serves_both_sides()
    {
        var services = new ServiceCollection().AddInMemoryTokenRevocation().BuildServiceProvider();

        services.GetRequiredService<ITokenRevocationEvaluator>()
            .Should().BeSameAs(services.GetRequiredService<ITokenRevocationWriter>());
    }

    [Fact]
    public async Task What_the_writer_records_the_evaluator_sees()
    {
        var services = new ServiceCollection().AddInMemoryTokenRevocation().BuildServiceProvider();
        var writer = services.GetRequiredService<ITokenRevocationWriter>();
        var evaluator = services.GetRequiredService<ITokenRevocationEvaluator>();

        await writer.RevokeTokenAsync("token-a", DateTimeOffset.UtcNow.AddHours(1), "signed out");

        var verdict = await evaluator.EvaluateAsync(new TokenIdentity
        {
            TokenId = "token-a",
            SubjectId = "subject-a",
            IssuedAt = DateTimeOffset.UtcNow
        });

        verdict.IsRevoked.Should().BeTrue();
        verdict.Reason.Should().Be(RevocationReason.TokenRevoked);
    }

    [Fact]
    public async Task An_untouched_token_stands()
    {
        var services = new ServiceCollection().AddInMemoryTokenRevocation().BuildServiceProvider();

        var verdict = await services.GetRequiredService<ITokenRevocationEvaluator>()
            .EvaluateAsync(new TokenIdentity
            {
                TokenId = "token-b",
                SubjectId = "subject-b",
                IssuedAt = DateTimeOffset.UtcNow
            });

        verdict.IsRevoked.Should().BeFalse();
    }

    [Fact]
    public void The_redis_store_serves_both_sides()
    {
        var services = new ServiceCollection()
            .AddSingleton(Substitute.For<IConnectionMultiplexer>())
            .AddRedisTokenRevocation(TimeSpan.FromHours(24))
            .BuildServiceProvider();

        services.GetRequiredService<ITokenRevocationEvaluator>()
            .Should().BeSameAs(services.GetRequiredService<ITokenRevocationWriter>());
    }

    [Fact]
    public void A_cutoff_lifetime_shorter_than_a_token_is_refused()
    {
        // A cutoff that expires before the tokens it refuses lets them back in.
        var register = () => new ServiceCollection().AddRedisTokenRevocation(TimeSpan.Zero);

        register.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// A deployment that declared it does no revocation still has to satisfy
    /// the writing side, and calling it has to say what is wrong.
    /// </summary>
    /// <remarks>
    /// A writer that quietly accepted the call would report "signed out
    /// everywhere" to a person whose tokens keep working.
    /// </remarks>
    [Fact]
    public async Task Turning_revocation_off_leaves_a_writer_that_refuses_to_pretend()
    {
        var services = new ServiceCollection()
            .AddNoTokenRevocation("access tokens live 60 seconds")
            .BuildServiceProvider();

        var revoke = () => services.GetRequiredService<ITokenRevocationWriter>()
            .RevokeTokenAsync("token-c", DateTimeOffset.UtcNow.AddHours(1), "signed out");

        (await revoke.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*access tokens live 60 seconds*");
    }
}
