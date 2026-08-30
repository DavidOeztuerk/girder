using System.Diagnostics;

namespace Girder.Abstractions.Observability;

/// <summary>
/// The name one request goes by while it is being served, wherever it goes.
/// </summary>
/// <remarks>
/// One request usually touches several services, and this is what stitches
/// their logs back into one story. Written as baggage rather than as a tag: a
/// tag stays on the span it was written to, and baggage is what travels.
/// </remarks>
public static class CorrelationId
{
    /// <summary>The header the id travels in, inbound and outbound.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>The baggage key, and the log property that carries it.</summary>
    public const string BaggageKey = "CorrelationId";

    /// <summary>
    /// The id of the request being served, or <c>null</c> outside one.
    /// </summary>
    /// <remarks>
    /// Null is an answer, not a gap to fill. A background job correlates with
    /// nothing, and minting an id per outgoing call would produce a story with
    /// one sentence in every line.
    /// </remarks>
    public static string? Current => Activity.Current?.GetBaggageItem(BaggageKey);
}
