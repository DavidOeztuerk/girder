using Girder.Abstractions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Girder.Infrastructure.Tests.Hosting;

/// <summary>
/// The loop contract every periodic background service inherits.
/// </summary>
/// <remarks>
/// Deterministic throughout: the clock is moved by hand, so nothing here waits
/// on the wall clock or depends on how busy the machine is.
/// </remarks>
[Trait("Category", "Unit")]
public class PeriodicBackgroundServiceTests
{
    private sealed class Probe(
        TimeProvider time,
        TimeSpan interval,
        Func<int, Task>? onRun = null)
        : PeriodicBackgroundService(NullLogger.Instance, time)
    {
        private readonly List<TaskCompletionSource> _runs = [];
        private readonly Lock _gate = new();

        public int Runs { get; private set; }

        protected override TimeSpan Interval { get; } = interval;

        protected override async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            int run;
            lock (_gate)
            {
                run = ++Runs;
                foreach (var waiter in _runs) waiter.TrySetResult();
                _runs.Clear();
            }

            if (onRun is not null) await onRun(run);
        }

        /// <summary>Completes once the next run has begun.</summary>
        public Task NextRun()
        {
            lock (_gate)
            {
                var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _runs.Add(waiter);
                return waiter.Task;
            }
        }
    }

    /// <summary>Waits for <paramref name="task"/> with a ceiling, so a hang fails rather than hangs.</summary>
    private static async Task Within(Task task, string because) =>
        (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(10))))
            .Should().BeSameAs(task, because);

    /// <summary>
    /// Moves the clock forward by <paramref name="interval"/> until
    /// <paramref name="task"/> completes.
    /// </summary>
    /// <remarks>
    /// A single Advance is not enough on its own: the loop registers its timer
    /// only after the run returns, and advancing before that leaves the timer
    /// starting from the new "now". Repeating closes that window without ever
    /// waiting on the wall clock — what is asserted stays "the interval passed,
    /// so it ran again", never "it ran within N milliseconds".
    /// </remarks>
    private static async Task AdvanceUntil(FakeTimeProvider time, TimeSpan interval, Task task, string because)
    {
        for (var attempt = 0; attempt < 200 && !task.IsCompleted; attempt++)
        {
            time.Advance(interval);
            await Task.Delay(5);
        }

        await Within(task, because);
    }

    [Fact]
    public async Task The_first_run_happens_at_startup_not_after_one_interval()
    {
        var time = new FakeTimeProvider();
        var probe = new Probe(time, TimeSpan.FromHours(6));

        var first = probe.NextRun();
        await probe.StartAsync(CancellationToken.None);

        await Within(first, "work must start immediately, not six hours later");
        probe.Runs.Should().Be(1);

        await probe.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_next_run_happens_when_the_interval_has_passed()
    {
        var time = new FakeTimeProvider();
        var probe = new Probe(time, TimeSpan.FromHours(6));

        var first = probe.NextRun();
        await probe.StartAsync(CancellationToken.None);
        await Within(first, "first run");

        var second = probe.NextRun();
        await AdvanceUntil(time, TimeSpan.FromHours(6), second,
            "the interval elapsed, so the work runs again");
        probe.Runs.Should().Be(2);

        await probe.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Nothing_runs_again_before_the_interval_has_passed()
    {
        var time = new FakeTimeProvider();
        var probe = new Probe(time, TimeSpan.FromHours(6));

        var first = probe.NextRun();
        await probe.StartAsync(CancellationToken.None);
        await Within(first, "first run");

        time.Advance(TimeSpan.FromHours(5));

        probe.Runs.Should().Be(1, "five hours is not six");

        await probe.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_failing_run_does_not_end_the_loop()
    {
        // The service that gives up after one bad pass looks alive and does
        // nothing until the process restarts.
        var time = new FakeTimeProvider();
        var probe = new Probe(time, TimeSpan.FromMinutes(1), onRun: run =>
            run == 1 ? throw new InvalidOperationException("first pass fails") : Task.CompletedTask);

        var first = probe.NextRun();
        await probe.StartAsync(CancellationToken.None);
        await Within(first, "first run");

        var second = probe.NextRun();
        await AdvanceUntil(time, TimeSpan.FromMinutes(1), second,
            "the loop must survive a failed run");
        probe.Runs.Should().Be(2);

        await probe.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stopping_ends_the_loop_while_it_waits()
    {
        var time = new FakeTimeProvider();
        var probe = new Probe(time, TimeSpan.FromDays(1));

        var first = probe.NextRun();
        await probe.StartAsync(CancellationToken.None);
        await Within(first, "first run");

        // Waiting a day; stopping must not take a day.
        await Within(probe.StopAsync(CancellationToken.None), "cancellation must interrupt the wait");
    }

    [Fact]
    public async Task Stopping_ends_the_loop_while_work_is_in_flight()
    {
        var time = new FakeTimeProvider();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new Probe(time, TimeSpan.FromMinutes(1), onRun: _ => blocked.Task);

        var first = probe.NextRun();
        await probe.StartAsync(CancellationToken.None);
        await Within(first, "first run");

        var stopping = probe.StopAsync(CancellationToken.None);
        blocked.SetResult();

        await Within(stopping, "stop must complete once the in-flight run returns");
        probe.Runs.Should().Be(1, "no further run may start after stopping");
    }

    [Fact]
    public async Task A_cancelled_token_before_start_runs_nothing()
    {
        var time = new FakeTimeProvider();
        var probe = new Probe(time, TimeSpan.FromMinutes(1));

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await probe.StartAsync(cts.Token);
        await probe.StopAsync(CancellationToken.None);

        probe.Runs.Should().Be(0);
    }
}
