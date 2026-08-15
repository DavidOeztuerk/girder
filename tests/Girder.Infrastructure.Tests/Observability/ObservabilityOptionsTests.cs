using Girder.Infrastructure.Observability;

namespace Girder.Infrastructure.Tests.Observability;

[Trait("Category", "Unit")]
public class ObservabilityOptionsTests
{
    [Fact]
    public void SectionName_ShouldBeObservability()
    {
        ObservabilityOptions.SectionName.Should().Be("Observability");
    }

    [Fact]
    public void Defaults_CaptureDatabaseStatements_ShouldBeFalse()
    {
        var options = new ObservabilityOptions();
        options.CaptureDatabaseStatements.Should().BeFalse();
    }

    [Fact]
    public void Defaults_EnableDetailedHttpLogging_ShouldBeFalse()
    {
        var options = new ObservabilityOptions();
        options.EnableDetailedHttpLogging.Should().BeFalse();
    }

    [Fact]
    public void Defaults_SlowRequestLogThresholdMs_ShouldBe1000()
    {
        var options = new ObservabilityOptions();
        options.SlowRequestLogThresholdMs.Should().Be(1000);
    }

    [Fact]
    public void Properties_ShouldBeSettable()
    {
        var options = new ObservabilityOptions
        {
            CaptureDatabaseStatements = true,
            EnableDetailedHttpLogging = true,
            SlowRequestLogThresholdMs = 500
        };

        options.CaptureDatabaseStatements.Should().BeTrue();
        options.EnableDetailedHttpLogging.Should().BeTrue();
        options.SlowRequestLogThresholdMs.Should().Be(500);
    }
}
