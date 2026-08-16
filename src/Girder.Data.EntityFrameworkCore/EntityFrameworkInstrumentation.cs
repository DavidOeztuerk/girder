using OpenTelemetry.Trace;

namespace Girder.Data.EntityFrameworkCore;

public static class EntityFrameworkInstrumentation
{
    /// <summary>
    /// Traces EF Core commands.
    /// </summary>
    /// <param name="captureStatements">
    /// Whether the SQL text is recorded on the span. Off by default: a
    /// statement carries table names, parameter values and therefore personal
    /// data, and spans travel to wherever telemetry is collected.
    /// </param>
    /// <remarks>
    /// Pass this to <c>TelemetryBuilder.AddTracing</c>:
    /// <code>
    /// builder.AddTracing(t => t.AddGirderEntityFrameworkInstrumentation());
    /// </code>
    /// The instrumentation package exposes no option for statement capture, so
    /// suppression works by clearing the tag after it was set.
    /// </remarks>
    public static TracerProviderBuilder AddGirderEntityFrameworkInstrumentation(
        this TracerProviderBuilder builder,
        bool captureStatements = false) =>
        builder.AddEntityFrameworkCoreInstrumentation(options =>
        {
            if (captureStatements) return;

            options.EnrichWithIDbCommand = (activity, _) =>
            {
                activity.SetTag("db.statement", null);
                activity.SetTag("db.query.text", null);
            };
        });
}
