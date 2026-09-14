using System.Text.RegularExpressions;
using Noelia.Dashboard;

namespace Noelia.Infrastructure.Tests.Architecture;

/// <summary>Pins the dashboard's zero-third-party-package boundary.</summary>
[Trait("Category", "Unit")]
public sealed class DashboardPackageStaysThinTests
{
    [Fact]
    public void The_dashboard_declares_no_package_reference()
    {
        var project = File.ReadAllText(ProjectFile());

        Regex.Matches(project, @"<PackageReference Include=""([^""]+)""")
            .Should().BeEmpty();
    }

    [Fact]
    public void The_dashboard_compiles_only_against_the_framework_and_abstractions()
    {
        string[] allowed =
        [
            "System",
            "Microsoft.AspNetCore",
            "Microsoft.Extensions",
            "Microsoft.Net.Http",
            "netstandard",
            "Noelia.Abstractions"
        ];

        var foreign = typeof(INoeliaDashboard).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name!)
            .Where(name => !allowed.Any(
                prefix => name == prefix || name.StartsWith($"{prefix}.", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreign.Should().BeEmpty();
    }

    private static string ProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", "Noelia.Dashboard", "Noelia.Dashboard.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Noelia.Dashboard.csproj was not found.");
    }
}
