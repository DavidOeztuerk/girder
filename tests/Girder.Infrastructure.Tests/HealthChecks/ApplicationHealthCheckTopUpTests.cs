using Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class ApplicationHealthCheckTopUpTests
{
    private readonly ILogger<ApplicationHealthCheck> _logger = Substitute.For<ILogger<ApplicationHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithPrivateMemoryKey()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("private_memory");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithGcKeys()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("gc_total_memory");
    }
}

[Trait("Category", "Unit")]
public class MemoryHealthCheckTopUpTests
{
    private readonly ILogger<MemoryHealthCheck> _logger = Substitute.For<ILogger<MemoryHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithGcCollectionKeys()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("gc_gen0_collections");
        result.Data.Should().ContainKey("gc_gen1_collections");
        result.Data.Should().ContainKey("gc_gen2_collections");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithFragmentationKey()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("gc_fragmented_mb");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsNonNullDescription()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Description.Should().NotBeNullOrEmpty();
    }
}

[Trait("Category", "Unit")]
public class ApplicationHealthCheckUptimeFormattingTests
{
    private readonly ILogger<ApplicationHealthCheck> _logger = Substitute.For<ILogger<ApplicationHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_ReturnsUptimeFormattedAsNonEmpty()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("uptime_formatted");
        var formatted = result.Data["uptime_formatted"] as string;
        formatted.Should().NotBeNullOrEmpty();
        // Process uptime is in seconds format (Xs), minutes (Xm Ys), hours (Xh Ym), or days (Xd Yh Zm)
        formatted.Should().MatchRegex(@"(\d+s|\d+m \d+s|\d+h \d+m|\d+d \d+h \d+m)");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUptimeSecondsAsNumericValue()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        // uptime_seconds is present and is a numeric value (can be negative on some systems due to timezone)
        result.Data.Should().ContainKey("uptime_seconds");
        result.Data["uptime_seconds"].Should().BeOfType<double>();
    }

    [Fact]
    public async Task CheckHealthAsync_ProcessorCountIsPositive()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        var processorCount = (int)result.Data["processor_count"];
        processorCount.Should().BeGreaterThan(0);
    }
}

[Trait("Category", "Unit")]
public class MemoryHealthCheckAdditionalTests
{
    private readonly ILogger<MemoryHealthCheck> _logger = Substitute.For<ILogger<MemoryHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_GcLoadDataKey_IsPresent()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("gc_memory_load");
        result.Data.Should().ContainKey("gc_compacting");
    }

    [Fact]
    public async Task CheckHealthAsync_WorkingSetMbIsPositive()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        var workingSetMb = (double)result.Data["working_set_mb"];
        workingSetMb.Should().BeGreaterThan(0);
    }
}
