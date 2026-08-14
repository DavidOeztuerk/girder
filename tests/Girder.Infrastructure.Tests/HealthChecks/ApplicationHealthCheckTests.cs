using Girder.Infrastructure.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class ApplicationHealthCheckTests
{
    private readonly ILogger<ApplicationHealthCheck> _logger = Substitute.For<ILogger<ApplicationHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthyOrDegraded()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        // Application just started so might be Degraded ("recently started") or Healthy
        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithExpectedKeys()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("uptime_seconds");
        result.Data.Should().ContainKey("uptime_formatted");
        result.Data.Should().ContainKey("process_id");
        result.Data.Should().ContainKey("process_name");
        result.Data.Should().ContainKey("machine_name");
        result.Data.Should().ContainKey("processor_count");
        result.Data.Should().ContainKey("framework_version");
        result.Data.Should().ContainKey("gc_total_memory");
        result.Data.Should().ContainKey("working_set");
        result.Data.Should().ContainKey("thread_count");
    }

    [Fact]
    public async Task CheckHealthAsync_DescriptionContainsText()
    {
        var check = new ApplicationHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Application", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Description.Should().NotBeNullOrEmpty();
    }
}

[Trait("Category", "Unit")]
public class MemoryHealthCheckTests
{
    private readonly ILogger<MemoryHealthCheck> _logger = Substitute.For<ILogger<MemoryHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_ReturnsResult()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithMemoryKeys()
    {
        var check = new MemoryHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("Memory", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("working_set_mb");
        result.Data.Should().ContainKey("private_memory_mb");
        result.Data.Should().ContainKey("managed_memory_mb");
        result.Data.Should().ContainKey("gc_heap_size_mb");
    }
}

[Trait("Category", "Unit")]
public class DiskSpaceHealthCheckTests
{
    private readonly ILogger<DiskSpaceHealthCheck> _logger = Substitute.For<ILogger<DiskSpaceHealthCheck>>();

    [Fact]
    public async Task CheckHealthAsync_ReturnsResult()
    {
        var check = new DiskSpaceHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("DiskSpace", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().BeOneOf(HealthStatus.Healthy, HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDataWithDriveInfo()
    {
        var check = new DiskSpaceHealthCheck(_logger);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("DiskSpace", check, null, null)
        };

        var result = await check.CheckHealthAsync(context);

        result.Data.Should().ContainKey("drives");
        result.Data.Should().ContainKey("total_drives");
    }
}
