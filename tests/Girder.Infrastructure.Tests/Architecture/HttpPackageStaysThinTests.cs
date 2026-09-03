using System.Text.RegularExpressions;
using Girder.Infrastructure.Middleware;

namespace Girder.Infrastructure.Tests.Architecture;

/// <summary>
/// Pins that <c>Girder.Http</c> stays free of third-party packages.
/// </summary>
/// <remarks>
/// <para>The package exists for one reason: a service that wants a correlation
/// id and a rate limiter should not have to take Swagger, a telemetry pipeline
/// and nine logging packages to get them. Measured at the split,
/// <c>Girder.Infrastructure</c> pulled <strong>44</strong> transitive packages
/// and <c>Girder.Http</c> pulled <strong>none</strong>.</para>
///
/// <para>That number is the whole value, and nothing about a
/// <c>&lt;PackageReference&gt;</c> announces that it destroyed it. One line
/// added here in a hurry, and the next reader has no way to tell that the
/// package used to be worth taking on its own — so the guard is a test rather
/// than a sentence in a readme.</para>
/// </remarks>
[Trait("Category", "Unit")]
public class HttpPackageStaysThinTests
{
    [Fact]
    public void The_http_package_declares_no_package_reference_at_all()
    {
        var project = ProjectFile("Girder.Http");

        var declared = Regex.Matches(project, @"<PackageReference Include=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        declared.Should().BeEmpty(
            "the point of Girder.Http is that taking it costs nothing but the "
            + "shared framework; a package reference here travels to every "
            + "consumer that took it to avoid exactly that");
    }

    /// <summary>
    /// And nothing it compiles against beyond the framework and the ports.
    /// </summary>
    /// <remarks>
    /// The project-file check above misses a dependency that arrives through a
    /// project reference. This one reads the built assembly, so both routes are
    /// covered.
    /// </remarks>
    [Fact]
    public void The_http_package_compiles_against_the_framework_and_the_ports_only()
    {
        string[] allowed =
        [
            "System",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Microsoft.Net.Http",
            "netstandard",
            "Girder.Abstractions"
        ];

        var foreign = typeof(CorrelationIdMiddleware).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => !allowed.Any(
                prefix => name == prefix || name.StartsWith($"{prefix}.", StringComparison.Ordinal)))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreign.Should().BeEmpty();
    }

    /// <summary>
    /// The counter-probe on the claim itself: the engine really is the heavy one.
    /// </summary>
    /// <remarks>
    /// Without this, the test above would still pass if somebody moved the whole
    /// engine into the thin package — the comparison is what makes the split
    /// mean anything.
    /// </remarks>
    [Fact]
    public void The_engine_is_the_one_carrying_the_weight()
    {
        var engine = Regex.Matches(
            ProjectFile("Girder.Infrastructure"),
            @"<PackageReference Include=""([^""]+)""").Count;

        engine.Should().BeGreaterThan(
            10,
            "if this ever drops, the split has been undone from the other side");
    }

    private static string ProjectFile(string projectName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", projectName, $"{projectName}.csproj");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"{projectName}.csproj not found above {AppContext.BaseDirectory}");
    }
}
