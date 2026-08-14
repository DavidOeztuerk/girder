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

    #region GetConnectionString — Tier 3: Individual Components from Environment Variables

    [Fact]
    public void GetConnectionString_WhenNoConnectionStrings_BuildsFromEnvVarComponents()
    {
        ClearAllEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_HOST", "envhost");
            Environment.SetEnvironmentVariable("POSTGRES_DB", "envdb");
            Environment.SetEnvironmentVariable("POSTGRES_USER", "envuser");
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "envpass");
            Environment.SetEnvironmentVariable("POSTGRES_PORT", "5433");

            var config = BuildConfig(new Dictionary<string, string?>());

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be("Host=envhost;Database=envdb;Username=envuser;Password=envpass;Port=5433;Trust Server Certificate=true");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Tier 3: Individual Components from Configuration

    [Fact]
    public void GetConnectionString_WhenNoConnectionStrings_BuildsFromConfigComponents()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Host"] = "confighost",
                ["Database:Database"] = "configdb",
                ["Database:Username"] = "configuser",
                ["Database:Password"] = "configpass",
                ["Database:Port"] = "5434"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Be("Host=confighost;Database=configdb;Username=configuser;Password=configpass;Port=5434;Trust Server Certificate=true");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Tier 3: Component Defaults

    [Fact]
    public void GetConnectionString_ComponentDefaults_HostDefaultsToPostgresServiceName()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Password"] = "testpass"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Contain("Host=postgres_testservice");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_ComponentDefaults_DatabaseDefaultsToServiceNameLowercase()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Password"] = "testpass"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Contain("Database=testservice");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_ComponentDefaults_UsernameDefaultsToGirder()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Password"] = "testpass"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Contain("Username=girder");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_ComponentDefaults_PortDefaultsTo5432()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Password"] = "testpass"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Contain("Port=5432");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_ComponentDefaults_IncludesTrustServerCertificate()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Password"] = "testpass"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            result.Should().Contain("Trust Server Certificate=true");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Tier 3: Missing Password Throws

    [Fact]
    public void GetConnectionString_WhenNoPasswordAnywhere_ThrowsInvalidOperationException()
    {
        ClearAllEnvVars();
        try
        {
            var config = BuildConfig(new Dictionary<string, string?>());

            var act = () => InvokeGetConnectionString(config, "TestService");

            act.Should().Throw<TargetInvocationException>()
                .WithInnerException<InvalidOperationException>()
                .WithMessage("*password*not configured*");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion

    #region GetConnectionString — Tier 3: Mixed Sources

    [Fact]
    public void GetConnectionString_MixedSources_EnvVarComponentsOverrideConfigComponents()
    {
        ClearAllEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_HOST", "envhost");
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "envpass");

            var config = BuildConfig(new Dictionary<string, string?>
            {
                ["Database:Host"] = "confighost",
                ["Database:Database"] = "configdb",
                ["Database:Username"] = "configuser",
                ["Database:Password"] = "configpass",
                ["Database:Port"] = "5434"
            });

            var result = InvokeGetConnectionString(config, "TestService");

            // Host from env var, Database from config, Username from config, Password from env var, Port from config
            result.Should().Contain("Host=envhost");
            result.Should().Contain("Database=configdb");
            result.Should().Contain("Username=configuser");
            result.Should().Contain("Password=envpass");
            result.Should().Contain("Port=5434");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    [Fact]
    public void GetConnectionString_MixedSources_SomeEnvVarsSomeDefaults()
    {
        ClearAllEnvVars();
        try
        {
            Environment.SetEnvironmentVariable("POSTGRES_PASSWORD", "envpass");
            Environment.SetEnvironmentVariable("POSTGRES_PORT", "9999");

            var config = BuildConfig(new Dictionary<string, string?>());

            var result = InvokeGetConnectionString(config, "MyService");

            // Host defaults to postgres_{servicename}, Database defaults to servicename,
            // Username defaults to girder, Password from env, Port from env
            result.Should().Contain("Host=postgres_myservice");
            result.Should().Contain("Database=myservice");
            result.Should().Contain("Username=girder");
            result.Should().Contain("Password=envpass");
            result.Should().Contain("Port=9999");
        }
        finally
        {
            ClearAllEnvVars();
        }
    }

    #endregion

    #region ConfigureDatabaseOptions

    [Fact]
    public void ConfigureDatabaseOptions_BindsConfigSection()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Database:Host"] = "myhost",
            ["Database:Port"] = "5555",
            ["Database:Username"] = "testuser",
            ["Database:Password"] = "testpass"
        });

        var services = new ServiceCollection();
        services.ConfigureDatabaseOptions(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        options.Host.Should().Be("myhost");
        options.Port.Should().Be(5555);
        options.Username.Should().Be("testuser");
        options.Password.Should().Be("testpass");
    }

    [Fact]
    public void ConfigureDatabaseOptions_ReturnsSameServiceCollection()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        var result = services.ConfigureDatabaseOptions(config);

        result.Should().BeSameAs(services);
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

            services.AddDatabaseContext<TestDbContext>(config, "TestDb");

            services.Should().Contain(d => d.ServiceType == typeof(TestDbContext));
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

    #region DatabaseOptions Defaults

    [Fact]
    public void DatabaseOptions_HostDefaultsToLocalhost()
    {
        var options = new DatabaseOptions();
        options.Host.Should().Be("localhost");
    }

    [Fact]
    public void DatabaseOptions_PortDefaultsTo5432()
    {
        var options = new DatabaseOptions();
        options.Port.Should().Be(5432);
    }

    [Fact]
    public void DatabaseOptions_EnableRetryDefaultsToTrue()
    {
        var options = new DatabaseOptions();
        options.EnableRetry.Should().BeTrue();
    }

    [Fact]
    public void DatabaseOptions_CommandTimeoutDefaultsTo30()
    {
        var options = new DatabaseOptions();
        options.CommandTimeout.Should().Be(30);
    }

    [Fact]
    public void DatabaseOptions_DatabaseDefaultsToEmptyString()
    {
        var options = new DatabaseOptions();
        options.Database.Should().BeEmpty();
    }

    [Fact]
    public void DatabaseOptions_UsernameDefaultsToEmptyString()
    {
        var options = new DatabaseOptions();
        options.Username.Should().BeEmpty();
    }

    [Fact]
    public void DatabaseOptions_PasswordDefaultsToEmptyString()
    {
        var options = new DatabaseOptions();
        options.Password.Should().BeEmpty();
    }

    [Fact]
    public void DatabaseOptions_MaxRetryCountDefaultsTo5()
    {
        var options = new DatabaseOptions();
        options.MaxRetryCount.Should().Be(5);
    }

    #endregion
}
