namespace Noelia.Infrastructure.Tests.Collections;

/// <summary>
/// Serializes tests that replace Serilog's process-wide logger or the process-wide
/// console writer. Those resources cannot be isolated by xUnit test instances.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerilogGlobalStateCollection
{
    public const string Name = "Serilog global state";
}
