using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class EncryptionExtensionsTests
{
    private static IServiceCollection BuildServicesWithRedis()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
        var database = Substitute.For<IDatabase>();
        connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(database);
        services.AddSingleton(connectionMultiplexer);

        return services;
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    #region AddDataEncryption(IConfiguration)

    [Fact]
    public void AddDataEncryption_WithConfiguration_RegistersKeyManagementService()
    {
        var services = BuildServicesWithRedis();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddDataEncryption(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IKeyManagementService));
    }

    [Fact]
    public void AddDataEncryption_WithConfiguration_RegistersDataEncryptionService()
    {
        var services = BuildServicesWithRedis();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddDataEncryption(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IDataEncryptionService));
    }

    [Fact]
    public void AddDataEncryption_WithConfiguration_RegistersFieldEncryptionService()
    {
        var services = BuildServicesWithRedis();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddDataEncryption(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IFieldEncryptionService));
    }

    [Fact]
    public void AddDataEncryption_WithConfiguration_RegistersKeyRotationBackgroundService()
    {
        var services = BuildServicesWithRedis();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddDataEncryption(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(KeyRotationBackgroundService));
    }

    [Fact]
    public void AddDataEncryption_WithConfiguration_RegistersKeyMaintenanceBackgroundService()
    {
        var services = BuildServicesWithRedis();
        var config = BuildConfig(new Dictionary<string, string?>());

        services.AddDataEncryption(config);

        services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType == typeof(KeyMaintenanceBackgroundService));
    }

    #endregion

    #region AddDataEncryption(Action delegates)

    [Fact]
    public void AddDataEncryption_WithActionDelegate_RegistersCoreServices()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(
            opts => { opts.DefaultAlgorithm = EncryptionAlgorithm.AES256GCM; });

        services.Should().Contain(d => d.ServiceType == typeof(IDataEncryptionService));
        services.Should().Contain(d => d.ServiceType == typeof(IKeyManagementService));
    }

    [Fact]
    public void AddDataEncryption_WithActionDelegate_AndKeyManagement_ConfiguresBoth()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(
            opts => { opts.DefaultAlgorithm = EncryptionAlgorithm.AES128GCM; },
            keyOpts => { keyOpts.AutoRotateKeys = true; });

        var provider = services.BuildServiceProvider();
        var encryptionOpts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        encryptionOpts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.AES128GCM);
        keyOpts.AutoRotateKeys.Should().BeTrue();
    }

    [Fact]
    public void AddDataEncryption_WithActionDelegate_NullKeyManagement_DoesNotThrow()
    {
        var services = BuildServicesWithRedis();

        var act = () => services.AddDataEncryption(
            opts => { opts.LogOperations = true; },
            null);

        act.Should().NotThrow();
    }

    #endregion

    #region AddDataEncryption(IEncryptionBuilder)

    [Fact]
    public void AddDataEncryption_WithBuilder_RegistersCoreServices()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
        {
            builder.UseEncryptionAlgorithm(EncryptionAlgorithm.AES256GCM);
        });

        services.Should().Contain(d => d.ServiceType == typeof(IDataEncryptionService));
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForDevelopment_SetsAES128GCM()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForDevelopment());

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.AES128GCM);
        opts.DefaultHashingAlgorithm.Should().Be(HashingAlgorithm.SHA256);
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForDevelopment_DisablesAutoRotation()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForDevelopment());

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.AutoRotateKeys.Should().BeFalse();
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForDevelopment_DisablesBackups()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForDevelopment());

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.AutoCreateBackups.Should().BeFalse();
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForDevelopment_Sets10MBMaxDataSize()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForDevelopment());

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.MaxDataSize.Should().Be(10 * 1024 * 1024);
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForProduction_SetsAES256GCM()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForProduction());

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.AES256GCM);
        opts.DefaultHashingAlgorithm.Should().Be(HashingAlgorithm.Argon2id);
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForProduction_EnablesAutoRotation()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForProduction());

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.AutoRotateKeys.Should().BeTrue();
        keyOpts.DefaultRotationInterval.Should().Be(TimeSpan.FromDays(90));
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForProduction_EnablesBackups()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForProduction());

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.AutoCreateBackups.Should().BeTrue();
    }

    [Fact]
    public void AddDataEncryption_WithBuilderForProduction_Sets100MBMaxDataSize()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder => builder.ForProduction());

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.MaxDataSize.Should().Be(100 * 1024 * 1024);
    }

    #endregion

    #region EncryptionBuilder fluent API

    [Fact]
    public void EncryptionBuilder_EnableAutoKeyRotation_SetsInterval()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.EnableAutoKeyRotation(TimeSpan.FromDays(30)));

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.AutoRotateKeys.Should().BeTrue();
        keyOpts.DefaultRotationInterval.Should().Be(TimeSpan.FromDays(30));
    }

    [Fact]
    public void EncryptionBuilder_UseEncryptionAlgorithm_SetsAlgorithm()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.UseEncryptionAlgorithm(EncryptionAlgorithm.ChaCha20Poly1305));

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.ChaCha20Poly1305);
    }

    [Fact]
    public void EncryptionBuilder_UseHashingAlgorithm_SetsAlgorithm()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.UseHashingAlgorithm(HashingAlgorithm.BCrypt));

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.DefaultHashingAlgorithm.Should().Be(HashingAlgorithm.BCrypt);
    }

    [Fact]
    public void EncryptionBuilder_EnableOperationLogging_SetsFlag()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.EnableOperationLogging(true));

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.LogOperations.Should().BeTrue();
    }

    [Fact]
    public void EncryptionBuilder_EnableOperationLogging_Disabled_SetsFlag()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.EnableOperationLogging(false));

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.LogOperations.Should().BeFalse();
    }

    [Fact]
    public void EncryptionBuilder_ConfigureMasterKeys_SetsMasterKey()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.ConfigureMasterKeys("master-key-value", "backup-key-value"));

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.MasterKey.Should().Be("master-key-value");
        keyOpts.BackupEncryptionKey.Should().Be("backup-key-value");
    }

    [Fact]
    public void EncryptionBuilder_ConfigureMasterKeys_WithoutBackup_SetsNullBackupKey()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.ConfigureMasterKeys("master-key-value"));

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.MasterKey.Should().Be("master-key-value");
        keyOpts.BackupEncryptionKey.Should().BeNull();
    }

    [Fact]
    public void EncryptionBuilder_ConfigureEncryption_AppliesChanges()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.ConfigureEncryption(opts =>
            {
                opts.DefaultAlgorithm = EncryptionAlgorithm.AES128GCM;
                opts.LogOperations = false;
            }));

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;

        opts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.AES128GCM);
        opts.LogOperations.Should().BeFalse();
    }

    [Fact]
    public void EncryptionBuilder_ConfigureKeyManagement_AppliesChanges()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder.ConfigureKeyManagement(opts =>
            {
                opts.AutoRotateKeys = true;
                opts.MaxKeyVersions = 3;
            }));

        var provider = services.BuildServiceProvider();
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        keyOpts.AutoRotateKeys.Should().BeTrue();
        keyOpts.MaxKeyVersions.Should().Be(3);
    }

    [Fact]
    public void EncryptionBuilder_AddKeyPurpose_DoesNotThrow()
    {
        var services = BuildServicesWithRedis();

        var act = () => services.AddDataEncryption(builder =>
            builder.AddKeyPurpose(KeyPurpose.Signing, new KeyGenerationOptions
            {
                Purpose = KeyPurpose.Signing,
                RotationInterval = TimeSpan.FromDays(30)
            }));

        act.Should().NotThrow();
    }

    [Fact]
    public void EncryptionBuilder_ChainedMethods_AllApply()
    {
        var services = BuildServicesWithRedis();

        services.AddDataEncryption(builder =>
            builder
                .UseEncryptionAlgorithm(EncryptionAlgorithm.AES256GCM)
                .UseHashingAlgorithm(HashingAlgorithm.Argon2id)
                .EnableAutoKeyRotation(TimeSpan.FromDays(60))
                .EnableOperationLogging(true)
                .ConfigureMasterKeys("test-key"));

        var provider = services.BuildServiceProvider();
        var encOpts = provider.GetRequiredService<IOptions<DataEncryptionOptions>>().Value;
        var keyOpts = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        encOpts.DefaultAlgorithm.Should().Be(EncryptionAlgorithm.AES256GCM);
        encOpts.DefaultHashingAlgorithm.Should().Be(HashingAlgorithm.Argon2id);
        encOpts.LogOperations.Should().BeTrue();
        keyOpts.AutoRotateKeys.Should().BeTrue();
        keyOpts.DefaultRotationInterval.Should().Be(TimeSpan.FromDays(60));
        keyOpts.MasterKey.Should().Be("test-key");
    }

    #endregion
}
