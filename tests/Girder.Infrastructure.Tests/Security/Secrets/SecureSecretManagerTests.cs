using Girder.Infrastructure.Security.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.Secrets;

[Trait("Category", "Unit")]
public class SecureSecretManagerTests
{
    private static SecureSecretManager CreateManager(
        string provider = "inmemory",
        IMemoryCache? cache = null)
    {
        var logger = Substitute.For<ILogger<SecureSecretManager>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretManager:Provider"] = provider
            })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        return new SecureSecretManager(logger, config, env, cache);
    }

    [Fact]
    public void Constructor_WithInMemoryProvider_CreatesInstance()
    {
        var manager = CreateManager("inmemory");

        manager.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithMemoryCache_CreatesInstance()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var manager = CreateManager("inmemory", cache);

        manager.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentKey_ReturnsNull()
    {
        var manager = CreateManager();

        var result = await manager.GetSecretAsync("nonexistent-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSecretAsync_ThenGetSecretAsync_ReturnsValue()
    {
        var manager = CreateManager();

        await manager.SetSecretAsync("my-key", "my-value");
        var result = await manager.GetSecretAsync("my-key");

        result.Should().Be("my-value");
    }

    [Fact]
    public async Task DeleteSecretAsync_ExistingKey_RemovesSecret()
    {
        var manager = CreateManager();

        await manager.SetSecretAsync("delete-key", "value");
        await manager.DeleteSecretAsync("delete-key");
        var result = await manager.GetSecretAsync("delete-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SecretExistsAsync_ExistingKey_ReturnsTrue()
    {
        var manager = CreateManager();

        await manager.SetSecretAsync("exists-key", "value");
        var result = await manager.SecretExistsAsync("exists-key");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_NonExistentKey_ReturnsFalse()
    {
        var manager = CreateManager();

        var result = await manager.SecretExistsAsync("missing-key");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetSecretNamesAsync_AfterSettingSecrets_ContainsKeys()
    {
        var manager = CreateManager();

        await manager.SetSecretAsync("key-one", "value-one");
        await manager.SetSecretAsync("key-two", "value-two");

        var names = (await manager.GetSecretNamesAsync()).ToList();

        names.Should().Contain("key-one");
        names.Should().Contain("key-two");
    }

    [Fact]
    public async Task RotateSecretAsync_ExistingSecret_ReturnsNewValue()
    {
        var manager = CreateManager();

        await manager.SetSecretAsync("rotatable", "original-value");
        var newValue = await manager.RotateSecretAsync("rotatable");

        newValue.Should().NotBeNullOrEmpty();
        newValue.Should().NotBe("original-value");
    }

    [Fact]
    public async Task GetSecretHistoryAsync_AfterMultipleSets_ReturnsVersions()
    {
        var manager = CreateManager();

        await manager.SetSecretAsync("versioned", "v1");
        await manager.SetSecretAsync("versioned", "v2");

        var history = (await manager.GetSecretHistoryAsync("versioned")).ToList();

        history.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetSecretHistoryAsync_NonExistentKey_ReturnsEmpty()
    {
        var manager = CreateManager();

        var history = await manager.GetSecretHistoryAsync("nonexistent");

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task Constructor_DevelopmentEnvWithUnknownProvider_FallsBackToInMemory()
    {
        var logger = Substitute.For<ILogger<SecureSecretManager>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretManager:Provider"] = "unknown-provider"
            })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        // In Development, falls back to InMemoryProvider
        var manager = new SecureSecretManager(logger, config, env);

        await manager.SetSecretAsync("test", "value");
        var result = await manager.GetSecretAsync("test");

        result.Should().Be("value");
    }

    [Fact]
    public async Task GetSecretAsync_WithMemoryCache_CachesValue()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var manager = CreateManager("inmemory", cache);

        await manager.SetSecretAsync("cached-key", "cached-value");

        // Get twice to exercise cache path
        var result1 = await manager.GetSecretAsync("cached-key");
        var result2 = await manager.GetSecretAsync("cached-key");

        result1.Should().Be("cached-value");
        result2.Should().Be("cached-value");
    }
}
