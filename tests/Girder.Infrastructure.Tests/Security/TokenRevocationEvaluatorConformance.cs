using Girder.Abstractions.Security;
using Girder.InMemory.Security;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// The behaviour every token revocation implementation must show, whatever it
/// stores in. Derive one fixture per implementation.
/// </summary>
/// <remarks>
/// The interfaces cannot express when a revocation takes effect, how a cutoff
/// behaves at a second boundary, or that concurrent cutoffs never move
/// backwards. Those promises live here, so a second implementation either keeps
/// them or fails.
/// </remarks>
public abstract class TokenRevocationEvaluatorConformance
{
    /// <summary>One store, read and write side, empty.</summary>
    protected abstract (ITokenRevocationEvaluator Reader, ITokenRevocationWriter Writer) CreateStore();

    /// <summary>
    /// Anchor for every test, on a whole second in the present.
    /// </summary>
    /// <remarks>
    /// Anchored to the current time rather than a fixed date because entries
    /// carry a real expiry: a store may drop anything already past, and a fixed
    /// date in the past would make every revocation expire the moment it is
    /// written. Truncated to a whole second so cutoff arithmetic is exact.
    /// </remarks>
    private static readonly DateTimeOffset Noon = DateTimeOffset.FromUnixTimeSeconds(
        DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    private static TokenIdentity TokenFor(
        string subject,
        DateTimeOffset issuedAt,
        string? tokenId = null,
        string? sessionId = null) =>
        new()
        {
            TokenId = tokenId ?? $"jti-{Guid.NewGuid():N}",
            SubjectId = subject,
            IssuedAt = issuedAt,
            SessionId = sessionId
        };

    private static string NewSubject() => $"sub-{Guid.NewGuid():N}";

    [Fact]
    public async Task An_untouched_token_stands()
    {
        var (reader, _) = CreateStore();

        var verdict = await reader.EvaluateAsync(TokenFor(NewSubject(), Noon));

        verdict.Should().Be(RevocationVerdict.Valid);
    }

    [Fact]
    public async Task A_revoked_token_is_refused_on_the_next_check()
    {
        var (reader, writer) = CreateStore();
        var token = TokenFor(NewSubject(), Noon);

        await writer.RevokeTokenAsync(token.TokenId, Noon.AddHours(1), "test");

        var verdict = await reader.EvaluateAsync(token);

        verdict.IsRevoked.Should().BeTrue();
        verdict.Reason.Should().Be(RevocationReason.TokenRevoked);
    }

    [Fact]
    public async Task Revoking_one_token_leaves_the_others_alone()
    {
        var (reader, writer) = CreateStore();
        var subject = NewSubject();
        var revoked = TokenFor(subject, Noon);
        var other = TokenFor(subject, Noon);

        await writer.RevokeTokenAsync(revoked.TokenId, Noon.AddHours(1), "test");

        (await reader.EvaluateAsync(other)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task Revoking_a_session_refuses_only_that_session()
    {
        var (reader, writer) = CreateStore();
        var subject = NewSubject();
        var onPhone = TokenFor(subject, Noon, sessionId: "phone");
        var onLaptop = TokenFor(subject, Noon, sessionId: "laptop");

        await writer.RevokeSessionAsync(subject, "phone", Noon.AddHours(1), "test");

        var phone = await reader.EvaluateAsync(onPhone);
        phone.IsRevoked.Should().BeTrue();
        phone.Reason.Should().Be(RevocationReason.SessionRevoked);

        (await reader.EvaluateAsync(onLaptop)).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task A_session_revocation_does_not_reach_another_subject()
    {
        var (reader, writer) = CreateStore();
        var mine = NewSubject();
        var theirs = NewSubject();

        await writer.RevokeSessionAsync(mine, "phone", Noon.AddHours(1), "test");

        (await reader.EvaluateAsync(TokenFor(theirs, Noon, sessionId: "phone")))
            .IsRevoked.Should().BeFalse("session ids are only unique within a subject");
    }

    [Fact]
    public async Task A_cutoff_refuses_tokens_the_store_never_saw()
    {
        // The point of a cutoff: it works without knowing which tokens exist.
        var (reader, writer) = CreateStore();
        var subject = NewSubject();

        await writer.RevokeSubjectBeforeAsync(subject, Noon, "password changed");

        var verdict = await reader.EvaluateAsync(TokenFor(subject, Noon.AddSeconds(-1)));

        verdict.IsRevoked.Should().BeTrue();
        verdict.Reason.Should().Be(RevocationReason.SubjectCutoff);
    }

    [Fact]
    public async Task A_token_issued_after_the_cutoff_stands()
    {
        // Signing in again right after "sign out everywhere" has to work.
        var (reader, writer) = CreateStore();
        var subject = NewSubject();

        await writer.RevokeSubjectBeforeAsync(subject, Noon, "test");

        (await reader.EvaluateAsync(TokenFor(subject, Noon.AddSeconds(1))))
            .IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task A_token_issued_within_the_cutoff_second_is_refused()
    {
        // iat has second granularity, so a cutoff at 12:00:00.400 cannot be
        // decided for a token stamped 12:00:00. Rounding up discards it — one
        // token too many rather than one too few.
        var (reader, writer) = CreateStore();
        var subject = NewSubject();

        await writer.RevokeSubjectBeforeAsync(subject, Noon.AddMilliseconds(400), "test");

        (await reader.EvaluateAsync(TokenFor(subject, Noon)))
            .IsRevoked.Should().BeTrue("the token was issued during the cutoff second");
    }

    [Fact]
    public async Task A_cutoff_does_not_reach_another_subject()
    {
        var (reader, writer) = CreateStore();
        var mine = NewSubject();
        var theirs = NewSubject();

        await writer.RevokeSubjectBeforeAsync(mine, Noon.AddHours(1), "test");

        (await reader.EvaluateAsync(TokenFor(theirs, Noon))).IsRevoked.Should().BeFalse();
    }

    [Fact]
    public async Task A_cutoff_never_moves_backwards()
    {
        var (reader, writer) = CreateStore();
        var subject = NewSubject();

        await writer.RevokeSubjectBeforeAsync(subject, Noon.AddHours(1), "later");
        await writer.RevokeSubjectBeforeAsync(subject, Noon, "earlier, must not win");

        (await reader.EvaluateAsync(TokenFor(subject, Noon.AddMinutes(30))))
            .IsRevoked.Should().BeTrue("the later cutoff still stands");
    }

    [Fact]
    public async Task Concurrent_cutoffs_settle_on_the_latest()
    {
        // Two administrators acting at once must not undo each other. The gate
        // is load-bearing: without it the writes run one after another and
        // never race. It is awaited rather than blocked on — a Barrier would
        // hold a pool thread per writer and starve the rest of the suite.
        var (reader, writer) = CreateStore();
        var subject = NewSubject();
        const int writers = 32;

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var racers = Enumerable.Range(0, writers).Select(async i =>
        {
            await gate.Task;
            await writer.RevokeSubjectBeforeAsync(subject, Noon.AddMinutes(i), "race");
        }).ToArray();

        gate.SetResult();
        await Task.WhenAll(racers);

        (await reader.EvaluateAsync(TokenFor(subject, Noon.AddMinutes(writers - 2))))
            .IsRevoked.Should().BeTrue("the latest cutoff must survive the race");
    }

    [Fact]
    public async Task Revoking_the_same_token_twice_is_harmless()
    {
        var (reader, writer) = CreateStore();
        var token = TokenFor(NewSubject(), Noon);

        await writer.RevokeTokenAsync(token.TokenId, Noon.AddHours(1), "test");
        await writer.RevokeTokenAsync(token.TokenId, Noon.AddHours(1), "test");

        (await reader.EvaluateAsync(token)).IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task A_token_without_a_session_claim_still_gets_the_cutoff()
    {
        // Tokens minted before the issuer set `sid` must not slip through.
        var (reader, writer) = CreateStore();
        var subject = NewSubject();

        await writer.RevokeSubjectBeforeAsync(subject, Noon.AddHours(1), "test");

        var verdict = await reader.EvaluateAsync(TokenFor(subject, Noon, sessionId: null));

        verdict.IsRevoked.Should().BeTrue();
        verdict.Reason.Should().Be(RevocationReason.SubjectCutoff);
    }

    [Fact]
    public async Task A_verdict_from_a_reachable_store_is_not_stale()
    {
        var (reader, writer) = CreateStore();
        var token = TokenFor(NewSubject(), Noon);

        await writer.RevokeTokenAsync(token.TokenId, Noon.AddHours(1), "test");

        (await reader.EvaluateAsync(token)).IsStale.Should().BeFalse();
    }
}

[Trait("Category", "Unit")]
public class InMemoryTokenRevocationConformanceTests : TokenRevocationEvaluatorConformance
{
    protected override (ITokenRevocationEvaluator, ITokenRevocationWriter) CreateStore()
    {
        var store = new InMemoryTokenRevocationStore();
        return (store, store);
    }
}
