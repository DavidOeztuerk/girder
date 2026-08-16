using System.Reflection;
using System.Text.RegularExpressions;
using Girder.Abstractions.Security;
using Girder.Infrastructure.Communication;

namespace Girder.Infrastructure.Tests.Architecture;

/// <summary>
/// Pins that the engine and the port package name no infrastructure provider.
/// </summary>
/// <remarks>
/// The build guard covers <c>Girder.Abstractions</c> by forbidding package
/// references outright. <c>Girder.Infrastructure</c> needs a softer rule — it
/// may depend on ASP.NET and EF Core abstractions, but never on a driver — and
/// that is only visible in the compiled assembly, so it is asserted here.
/// </remarks>
[Trait("Category", "Unit")]
public class ProviderIndependenceTests
{
    /// <summary>
    /// Drivers that bind a deployment to one product. Each belongs in its own
    /// Girder.&lt;Provider&gt; package.
    /// </summary>
    /// <remarks>
    /// Serilog.Sinks.Console and .File are deliberately absent: stdout and a
    /// local file reach no network and pin no vendor. A sink that ships logs to
    /// a server does belong here — Serilog.Sinks.Elasticsearch is covered by
    /// the Elasticsearch entry.
    /// </remarks>
    private static readonly string[] Providers =
    [
        "MassTransit",
        "RabbitMQ",
        "Npgsql",
        "StackExchange.Redis",
        "Elasticsearch",
        "Serilog.Sinks.Seq",
        "Serilog.Sinks.OpenSearch"
    ];

    private static IEnumerable<string> ReferencedAssemblies(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    /// <summary>
    /// Providers the engine still carries, and why.
    /// </summary>
    /// <remarks>
    /// Empty, and meant to stay that way. It is asserted exactly, so adding a
    /// provider to the engine fails here rather than quietly binding every
    /// consumer to it.
    /// </remarks>
    private static readonly string[] KnownEngineProviders = [];

    [Fact]
    public void The_engine_carries_no_provider_beyond_the_known_ones()
    {
        var found = ReferencedAssemblies(typeof(ServiceCommunicationManager).Assembly)
            .Where(name => Providers.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        found.Should().Equal(
            KnownEngineProviders,
            "a new provider in Girder.Infrastructure binds every consumer to it, "
            + "and removing the last known one should make this list empty");
    }

    [Fact]
    public void The_engine_declares_no_provider_package_beyond_the_known_ones()
    {
        // The assembly check above only sees providers the code actually uses.
        // A declared but unused package still travels to every consumer as a
        // transitive NuGet dependency, so the project file is checked too.
        var project = FindProjectFile("Girder.Infrastructure");

        var declared = Regex.Matches(project, @"<PackageReference Include=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Where(name => Providers.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        declared.Should().Equal(KnownEngineProviders);
    }

    /// <summary>Reads a project file by walking up from the test assembly.</summary>
    private static string FindProjectFile(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", projectName, $"{projectName}.csproj");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{projectName}.csproj not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void The_port_package_references_no_provider()
    {
        var offenders = ReferencedAssemblies(typeof(ITokenRevocationEvaluator).Assembly)
            .Where(name => Providers.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ToList();

        offenders.Should().BeEmpty("Girder.Abstractions holds ports, not implementations");
    }

    [Fact]
    public void The_port_package_references_only_platform_abstractions()
    {
        // Anything beyond the platform is a dependency every consumer inherits,
        // whether they use that port or not.
        var allowed = new[] { "System.", "Microsoft.Extensions.", "netstandard", "Girder.Core" };

        ReferencedAssemblies(typeof(ITokenRevocationEvaluator).Assembly)
            .Where(name => !allowed.Any(a => name.StartsWith(a, StringComparison.Ordinal)))
            .Should().BeEmpty();
    }
}
