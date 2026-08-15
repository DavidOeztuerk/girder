namespace Girder.Infrastructure.Tests;

/// <summary>
/// Retries an assertion until it holds or the timeout elapses.
/// </summary>
/// <remarks>
/// For hosted services, whose work happens on a background loop the test does
/// not control. The alternative — sleeping a fixed span and asserting once —
/// passes on an idle machine and fails when the suite is busy, which makes it
/// look like the code broke rather than the test.
/// </remarks>
public static class Eventually
{
    /// <summary>
    /// Runs <paramref name="assertion"/> until it stops throwing. Rethrows the
    /// last failure once <paramref name="timeout"/> is up, so the test reports
    /// what was actually wrong rather than a bare timeout.
    /// </summary>
    public static async Task HoldsAsync(
        Func<Task> assertion,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(assertion);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(20);

        while (true)
        {
            try
            {
                await assertion();
                return;
            }
            catch when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(interval);
            }
        }
    }
}
