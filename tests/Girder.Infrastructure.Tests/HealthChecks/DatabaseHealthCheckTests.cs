using Infrastructure.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.HealthChecks;

[Trait("Category", "Unit")]
public class DatabaseHealthCheckTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", check, null, null)
        };

    [Fact]
    public void Constructor_WithNullDbContext_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<DatabaseHealthCheck>>();
        var act = () => new DatabaseHealthCheck(null!, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var ctx = Substitute.For<DbContext>();
        var act = () => new DatabaseHealthCheck(ctx, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCannotConnect_ReturnsUnhealthy()
    {
        var ctx = Substitute.For<DbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck>>();
        var check = new DatabaseHealthCheck(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Cannot connect");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_ReturnsUnhealthy()
    {
        var ctx = Substitute.For<DbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("Connection failed"));
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck>>();
        var check = new DatabaseHealthCheck(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}

[Trait("Category", "Unit")]
public class DatabaseHealthCheckGenericTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database-generic", check, null, null)
        };

    [Fact]
    public void Constructor_WithNullDbContext_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<DatabaseHealthCheck<ConcreteTestDbContext>>>();
        var act = () => new DatabaseHealthCheck<ConcreteTestDbContext>(null!, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var ctx = Substitute.For<ConcreteTestDbContext>();
        var act = () => new DatabaseHealthCheck<ConcreteTestDbContext>(ctx, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenCannotConnect_ReturnsUnhealthy()
    {
        var ctx = Substitute.For<ConcreteTestDbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck<ConcreteTestDbContext>>>();
        var check = new DatabaseHealthCheck<ConcreteTestDbContext>(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_ReturnsUnhealthy()
    {
        var ctx = Substitute.For<ConcreteTestDbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("DB down"));
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck<ConcreteTestDbContext>>>();
        var check = new DatabaseHealthCheck<ConcreteTestDbContext>(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }
}

// Concrete test class (NSubstitute requires non-abstract, non-sealed for substitution of concrete types)
public class ConcreteTestDbContext : DbContext
{
    protected ConcreteTestDbContext() : base() { }
    public ConcreteTestDbContext(DbContextOptions<ConcreteTestDbContext> options) : base(options) { }
}

[Trait("Category", "Unit")]
public class DatabaseHealthCheckCannotConnectDescriptionTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database", check, null, null)
        };

    [Fact]
    public async Task CheckHealthAsync_WhenCannotConnect_DescriptionContainsCannotConnect()
    {
        var ctx = Substitute.For<DbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck>>();
        var check = new DatabaseHealthCheck(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Cannot connect to database");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_DescriptionContainsFailed()
    {
        var ctx = Substitute.For<DbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("DB timeout"));
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck>>();
        var check = new DatabaseHealthCheck(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("failed");
        result.Exception.Should().BeOfType<TimeoutException>();
    }
}

[Trait("Category", "Unit")]
public class DatabaseHealthCheckGenericHealthyPathTests
{
    private static HealthCheckContext CreateContext(IHealthCheck check) =>
        new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("database-generic", check, null, null)
        };

    [Fact]
    public async Task CheckHealthAsync_WhenCannotConnect_ReturnsUnhealthyWithContextTypeName()
    {
        var ctx = Substitute.For<ConcreteTestDbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>()).Returns(false);
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck<ConcreteTestDbContext>>>();
        var check = new DatabaseHealthCheck<ConcreteTestDbContext>(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("ConcreteTestDbContext");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenExceptionThrown_DescriptionContainsContextType()
    {
        var ctx = Substitute.For<ConcreteTestDbContext>();
        var dbFacade = Substitute.For<DatabaseFacade>(ctx);
        dbFacade.CanConnectAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Timeout"));
        ctx.Database.Returns(dbFacade);

        var logger = Substitute.For<ILogger<DatabaseHealthCheck<ConcreteTestDbContext>>>();
        var check = new DatabaseHealthCheck<ConcreteTestDbContext>(ctx, logger);
        var context = CreateContext(check);

        var result = await check.CheckHealthAsync(context);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("ConcreteTestDbContext");
        result.Exception.Should().BeOfType<TimeoutException>();
    }
}
