using System.Reflection;
using Girder.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Girder.Infrastructure.Tests.Extensions;

[Trait("Category", "Unit")]
[Collection("EnvironmentVariables")]
public class DatabaseExtensionsTests
{
    private static readonly string[] EnvVarsToClean =
    [
        "ConnectionStrings__TestService",
        "POSTGRES_HOST",
        "POSTGRES_DB",
        "POSTGRES_USER",
        "POSTGRES_PASSWORD",
        "POSTGRES_PORT"
    ];

    private static string InvokeGetConnectionString(IConfiguration configuration, string serviceName)
    {
        var method = typeof(DatabaseExtensions)
            .GetMethod("GetConnectionString", BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull("GetConnectionString must exist as a private static method");

        return (string)method!.Invoke(null, [configuration, serviceName])!;
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static void ClearAllEnvVars()
    {
        foreach (var key in EnvVarsToClean)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    #region GetConnectionString — Tier 1: Environment Variable

    [Fact]
    public void GetConnectionString_WhenEnvVarSet_ReturnsEnvVarValue()
    {
        ClearAllEnvVars();
        try
        {
            var expected = "Host=envhost;Database=envdb;Username=envuser;Password=envpass;Port=5432";
            Environment.SetEnvironmentVariable("ConnectionStrings__TestService", expected);

            var config = BuildConfig(new Dictionary<string, string?>());

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be(expected);
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_WhenEnvVarSet_IgnoresConfigConnectionStrings()
    {
        ClearAllEnvVars();
        try
        {
            var envValue = "Host=fromenv;Database=fromenv";
            Environment.SetEnvironmentVariable("ConnectionStrings__TestService", envValue);

            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TestService"] = "Host=fromconfig;Database=fromconfig",
                ["ConnectionStrings:DefaultConnection"] = "Host=default;Database=default"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be(envValue);
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Tier 2: Configuration ConnectionStrings

    [Fact]
    public void GetConnectionString_WhenNoEnvVar_ReturnsServiceSpecificConfigConnectionString()
    {
        ClearAllEnvVars();
        try
        {
            var expected = "Host=confighost;Database=configdb;Username=configuser;Password=configpass;Port=5432";
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TestService"] = expected
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be(expected);
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_WhenNoEnvVarNoServiceSpecific_ReturnsDefaultConnection()
    {
        ClearAllEnvVars();
        try
        {
            var expected = "Host=defaulthost;Database=defaultdb;Username=defaultuser;Password=defaultpass;Port=5432";
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = expected
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be(expected);
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_WhenServiceSpecificAndDefaultExist_PrefersServiceSpecific()
    {
        ClearAllEnvVars();
        try
        {
            var serviceSpecific = "Host=servicehost;Database=servicedb";
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TestService"] = serviceSpecific,
                ["ConnectionStrings:DefaultConnection"] = "Host=defaulthost;Database=defaultdb"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be(serviceSpecific);
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion


    #region AddDatabaseContext (DI Registration)

    [Fact]
    public void AddDatabaseContext_RegistersDbContext_InServiceCollection()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TestDb"] = "Host=localhost;Database=testdb;Username=test;Password=test"
            });
            var services = new ServiceCollection();
            services.AddLogging();
            var env = Substitute.For<IHostEnvironment>();
            env.EnvironmentName.Returns("Production");
            services.AddSingleton<IHostEnvironment>(env);

            services.AddDatabaseContext<TestDbContext>(config, "TestDb", (_, _) => { });

            services.Should().Contain(d => d.ServiceType == typeof(TestDbContext));
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void AddDatabaseContext_HandsTheResolvedConnectionStringToTheApplication()
    {
        // Girder resolves *where*; the application binds the provider. Nothing
        // in Girder decides which database engine that is.
        ClearAllEnvVars();
        try
        {
            const string expected = "Host=localhost;Database=testdb;Username=test;Password=test";
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TestDb"] = expected
            });
            var services = new ServiceCollection();
            services.AddLogging();
            var env = Substitute.For<IHostEnvironment>();
            env.EnvironmentName.Returns("Production");
            services.AddSingleton<IHostEnvironment>(env);

            string? received = null;
            services.AddDatabaseContext<TestDbContext>(config, "TestDb", (_, cs) => received = cs);

            // The callback runs when the options are built, not at registration.
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<DbContextOptions<TestDbContext>>();

            received.Should().Be(expected);
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void AddDatabaseContext_WithoutAnyConnectionString_Throws()
    {
        // The Postgres-shaped fallback that used to fill this gap invented a
        // connection string for a server nobody had named.
        ClearAllEnvVars();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var act = () => services.AddDatabaseContext<TestDbContext>(
                BuildConfig(new Dictionary<string, string?>()), "TestDb", (_, _) => { });

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ConnectionStrings__TestDb*");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    // Minimal DbContext for testing
    private sealed class TestDbContext : DbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
    }

    #endregion

}
