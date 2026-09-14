namespace Noelia.Abstractions.Hosting;

/// <summary>
/// What this service is running, and what it decided not to run.
/// </summary>
/// <remarks>
/// Registered in the container so a service can report its own composition — at
/// startup, or on a diagnostics endpoint. The reason for each departure travels
/// with it: a list of what is missing is a puzzle, a list of what is missing and
/// why is a decision someone can review.
/// </remarks>
public sealed class NoeliaComposition
{
    /// <summary>Records one composition.</summary>
    public NoeliaComposition(
        IReadOnlyList<NoeliaModule> included,
        IReadOnlyDictionary<NoeliaModule, string> excluded)
    {
        Included = included;
        Excluded = excluded;
    }

    /// <summary>The modules that were set up, in the order they were set up.</summary>
    public IReadOnlyList<NoeliaModule> Included { get; }

    /// <summary>
    /// The modules that were named and then left out, against the reason given
    /// for leaving them out.
    /// </summary>
    /// <remarks>
    /// A module nobody mentioned is in neither list. Silence is not a decision,
    /// and recording it as one would make the report longer and less true.
    /// </remarks>
    public IReadOnlyDictionary<NoeliaModule, string> Excluded { get; }
}
