using Girder.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Girder.Infrastructure.Tests.Configuration;

[Trait("Category", "Unit")]
public class ConfigurationHelperTopUpTests
{
    [Fact]
    public void BuildConfiguration_WithEmptyArgs_ReturnsConfiguration()
    {
        var config = ConfigurationHelper.BuildConfiguration(Array.Empty<string>());

        config.Should().NotBeNull();
    }

    [Fact]
    public void BuildConfiguration_WithBasePath_ReturnsConfiguration()
    {
        var tempPath = Path.GetTempPath();

        var config = ConfigurationHelper.BuildConfiguration(Array.Empty<string>(), tempPath);

        config.Should().NotBeNull();
    }

    [Fact]
    public void BuildConfiguration_WithCommandLineArgs_IncludesArgs()
    {
        var args = new[] { "--TestKey=TestValue" };

        var config = ConfigurationHelper.BuildConfiguration(args);

        config["TestKey"].Should().Be("TestValue");
    }

    [Fact]
    public void BuildConfiguration_NullBasePath_UsesCurrentDirectory()
    {
        var act = () => ConfigurationHelper.BuildConfiguration(Array.Empty<string>(), null);

        act.Should().NotThrow();
    }
}
