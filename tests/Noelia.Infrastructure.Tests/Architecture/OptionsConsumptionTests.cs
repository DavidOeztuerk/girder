using System.Reflection;
using System.Text.RegularExpressions;

namespace Noelia.Infrastructure.Tests.Architecture;

/// <summary>
/// Prevents configuration APIs that accept values no shipped service reads.
/// </summary>
[Trait("Category", "Unit")]
public class OptionsConsumptionTests
{
    private static readonly HashSet<string> OptionsPorts =
    [
        "Microsoft.Extensions.Options.IOptions`1",
        "Microsoft.Extensions.Options.IOptionsMonitor`1",
        "Microsoft.Extensions.Options.IOptionsSnapshot`1"
    ];

    [Fact]
    public void Every_registered_Noelia_options_type_has_a_consumer()
    {
        var root = RepositoryRoot();
        var source = string.Join(
            "\n",
            Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        var productTypes = ProductAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();
        var productTypeNames = productTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

        var registered = Regex.Matches(
                source,
                @"\.(?:Configure|AddOptions)\s*<\s*(?:[A-Za-z0-9_]+\.)*(?<name>[A-Za-z0-9_]+)\s*>")
            .Select(match => match.Groups["name"].Value)
            .Where(productTypeNames.Contains)
            .ToHashSet(StringComparer.Ordinal);

        registered.Should().NotBeEmpty("the guard must measure real registrations");

        var consumed = productTypes
            .SelectMany(type => type.GetConstructors(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(type => type.IsGenericType
                           && OptionsPorts.Contains(type.GetGenericTypeDefinition().FullName!))
            .Select(type => type.GetGenericArguments()[0].Name)
            .ToHashSet(StringComparer.Ordinal);

        registered.Except(consumed).Order().Should().BeEmpty(
            "registering options without a reader promises behaviour that does not exist");
    }

    private static IEnumerable<Assembly> ProductAssemblies() =>
        Directory.GetFiles(AppContext.BaseDirectory, "Noelia.*.dll")
            .Where(path => !Path.GetFileName(path).Contains(".Tests", StringComparison.Ordinal)
                           && !Path.GetFileName(path).Contains(".Fixtures", StringComparison.Ordinal))
            .Select(Assembly.LoadFrom);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Noelia.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Noelia.slnx not found above {AppContext.BaseDirectory}");
    }
}
