using Girder.Abstractions.Security;
using Microsoft.Extensions.Logging.Abstractions;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class DegradingTokenRevocationEvaluatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static TokenIdentity SomeToken(string id = "jti-1") => new()
    {
        TokenId = id,
        SubjectId = "sub-1",
        IssuedAt = Start
    };

    /// <summary>Answers as told, or throws once <see cref="Fail"/> is set.</summary>
    private sealed class StubEvaluator : ITokenRevocationEvaluator
    {
        public RevocationVerdict Answer { get; set; } = RevocationVerdict.Valid;
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        public ValueTask<RevocationVerdict> EvaluateAsync(
            TokenIdentity token,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail
                ? throw new InvalidOperationException("store unreachable")
                : new ValueTask<RevocationVerdict>(Answer);
        }
    }

    private static (DegradingTokenRevocationEvaluator Sut, StubEvaluator Inner, FakeTimeProvider Time)
        Create(RevocationDegradationOptions? options = null)
    {
        var inner = new StubEvaluator();
        var time = new FakeTimeProvider(Start);
        var sut = new DegradingTokenRevocationEvaluator(
            inner,
            options ?? new RevocationDegradationOptions(),
            NullLogger<DegradingTokenRevocationEvaluator>.Instance,
            time);
        return (sut, inner, time);
    }

    [Fact]
    public async Task While_the_store_answers_the_verdict_passes_through_unchanged()
    {
        var (sut, inner, _) = Create();
        inner.Answer = new RevocationVerdict(true, RevocationReason.TokenRevoked, false);

        var verdict = await sut.EvaluateAsync(SomeToken());

        verdict.IsRevoked.Should().BeTrue();
        verdict.Reason.Should().Be(RevocationReason.TokenRevoked);
        verdict.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task A_recent_verdict_survives_a_brief_outage_and_is_marked_stale()
    {
        var (sut, inner, time) = Create();
        inner.Answer = new RevocationVerdict(true, RevocationReason.TokenRevoked, false);
        await sut.EvaluateAsync(SomeToken());

        inner.Fail = true;
        time.Advance(TimeSpan.FromSeconds(5));

        var verdict = await sut.EvaluateAsync(SomeToken());

        verdict.IsRevoked.Should().BeTrue("a revoked token must stay revoked through an outage");
        verdict.Reason.Should().Be(RevocationReason.TokenRevoked);
        verdict.IsStale.Should().BeTrue("the answer no longer came from the store");
    }

    [Fact]
    public async Task Past_the_staleness_bound_the_cached_verdict_is_dropped()
    {
        var (sut, inner, time) = Create(new RevocationDegradationOptions
        {
            MaxStaleness = TimeSpan.FromSeconds(30)
        });
        await sut.EvaluateAsync(SomeToken());

        inner.Fail = true;
        time.Advance(TimeSpan.FromSeconds(31));

        var verdict = await sut.EvaluateAsync(SomeToken());

        verdict.Reason.Should().Be(RevocationReason.StoreUnavailable);
        verdict.IsRevoked.Should().BeTrue("Deny is the default once nothing is known");
    }

    [Fact]
    public async Task With_no_cached_state_the_default_refuses()
    {
        // A cold start during an outage: the safe answer is to refuse.
        var (sut, inner, _) = Create();
        inner.Fail = true;

        var verdict = await sut.EvaluateAsync(SomeToken());

        verdict.IsRevoked.Should().BeTrue();
        verdict.Reason.Should().Be(RevocationReason.StoreUnavailable);
        verdict.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task An_operator_may_choose_availability_over_the_check()
    {
        var (sut, inner, _) = Create(new RevocationDegradationOptions
        {
            OnUnknown = UnknownStatePolicy.Allow
        });
        inner.Fail = true;

        var verdict = await sut.EvaluateAsync(SomeToken());

        verdict.IsRevoked.Should().BeFalse();
        verdict.Reason.Should().Be(RevocationReason.StoreUnavailable);
        verdict.IsStale.Should().BeTrue("the caller must still be able to tell this apart");
    }

    [Fact]
    public async Task One_token_s_cached_verdict_is_never_served_for_another()
    {
        var (sut, inner, _) = Create();
        inner.Answer = RevocationVerdict.Valid;
        await sut.EvaluateAsync(SomeToken("jti-known"));

        inner.Fail = true;

        var verdict = await sut.EvaluateAsync(SomeToken("jti-unknown"));

        verdict.Reason.Should().Be(RevocationReason.StoreUnavailable);
        verdict.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public async Task A_slow_store_counts_as_unreachable()
    {
        // The check runs on every request, so exceeding the budget has to be
        // treated as an outage rather than waited out.
        var slow = new SlowEvaluator();
        var sut = new DegradingTokenRevocationEvaluator(
            slow,
            new RevocationDegradationOptions { Budget = TimeSpan.FromMilliseconds(20) },
            NullLogger<DegradingTokenRevocationEvaluator>.Instance);

        var verdict = await sut.EvaluateAsync(SomeToken());

        verdict.Reason.Should().Be(RevocationReason.StoreUnavailable);
    }

    [Fact]
    public async Task A_cancelled_request_is_not_mistaken_for_an_outage()
    {
        var slow = new SlowEvaluator();
        var sut = new DegradingTokenRevocationEvaluator(
            slow,
            new RevocationDegradationOptions { Budget = TimeSpan.FromSeconds(30) },
            NullLogger<DegradingTokenRevocationEvaluator>.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await sut.EvaluateAsync(SomeToken(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "the caller gave up, which is not the store failing");
    }

    private sealed class SlowEvaluator : ITokenRevocationEvaluator
    {
        public async ValueTask<RevocationVerdict> EvaluateAsync(
            TokenIdentity token,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return RevocationVerdict.Valid;
        }
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
