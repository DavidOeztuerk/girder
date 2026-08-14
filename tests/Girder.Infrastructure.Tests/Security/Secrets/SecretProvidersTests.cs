using Infrastructure.Security.Secrets;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Tests.Security.Secrets;

[Trait("Category", "Unit")]
public class InMemoryProviderTests
{
    private static InMemoryProvider CreateProvider()
    {
        var logger = Substitute.For<ILogger>();
        return new InMemoryProvider(logger);
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentKey_ReturnsNull()
    {
        var provider = CreateProvider();

        var result = await provider.GetSecretAsync("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSecretAsync_ThenGetSecretAsync_ReturnsValue()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("my-key", "my-value");
        var result = await provider.GetSecretAsync("my-key");

        result.Should().Be("my-value");
    }

    [Fact]
    public async Task DeleteSecretAsync_ExistingKey_RemovesIt()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("to-delete", "value");
        await provider.DeleteSecretAsync("to-delete");
        var result = await provider.GetSecretAsync("to-delete");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteSecretAsync_NonExistentKey_DoesNotThrow()
    {
        var provider = CreateProvider();

        var act = async () => await provider.DeleteSecretAsync("nonexistent");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SecretExistsAsync_ExistingKey_ReturnsTrue()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("exists-key", "value");
        var result = await provider.SecretExistsAsync("exists-key");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_NonExistentKey_ReturnsFalse()
    {
        var provider = CreateProvider();

        var result = await provider.SecretExistsAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListSecretKeysAsync_AfterSettingKeys_ReturnsAllKeys()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("key-a", "val-a");
        await provider.SetSecretAsync("key-b", "val-b");

        var keys = (await provider.ListSecretKeysAsync()).ToList();

        keys.Should().Contain("key-a");
        keys.Should().Contain("key-b");
    }

    [Fact]
    public async Task ListSecretKeysAsync_EmptyProvider_ReturnsEmptyCollection()
    {
        var provider = CreateProvider();

        var keys = await provider.ListSecretKeysAsync();

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task SetSecretAsync_OverwriteExistingKey_UpdatesValue()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("key", "original");
        await provider.SetSecretAsync("key", "updated");

        var result = await provider.GetSecretAsync("key");

        result.Should().Be("updated");
    }
}

[Trait("Category", "Unit")]
public class EnvironmentVariableProviderTests
{
    [Fact]
    public void Constructor_DefaultPrefix_CreatesInstance()
    {
        var logger = Substitute.For<ILogger>();

        var provider = new EnvironmentVariableProvider(logger);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_CustomPrefix_CreatesInstance()
    {
        var logger = Substitute.For<ILogger>();

        var provider = new EnvironmentVariableProvider(logger, "MYAPP_");

        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentVariable_ReturnsNull()
    {
        var logger = Substitute.For<ILogger>();
        var provider = new EnvironmentVariableProvider(logger, "NONEXISTENT_PREFIX_XYZ_");

        var result = await provider.GetSecretAsync("MISSING_KEY");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ExistingEnvironmentVariable_ReturnsValue()
    {
        var prefix = "TEST_SECRET_PROVIDER_";
        var key = "MY_KEY";
        var envVarName = $"{prefix}{key}";
        Environment.SetEnvironmentVariable(envVarName, "test-value");

        try
        {
            var logger = Substitute.For<ILogger>();
            var provider = new EnvironmentVariableProvider(logger, prefix);

            var result = await provider.GetSecretAsync(key);

            result.Should().Be("test-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task SecretExistsAsync_NonExistentVariable_ReturnsFalse()
    {
        var logger = Substitute.For<ILogger>();
        var provider = new EnvironmentVariableProvider(logger, "NONEXISTENT_PREFIX_XYZ_");

        var result = await provider.SecretExistsAsync("MISSING_KEY");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecretExistsAsync_ExistingVariable_ReturnsTrue()
    {
        var prefix = "TEST_SECRET_EXISTS_";
        var key = "SOME_KEY";
        var envVarName = $"{prefix}{key}";
        Environment.SetEnvironmentVariable(envVarName, "value");

        try
        {
            var logger = Substitute.For<ILogger>();
            var provider = new EnvironmentVariableProvider(logger, prefix);

            var result = await provider.SecretExistsAsync(key);

            result.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task DeleteSecretAsync_AnyKey_DoesNotThrow()
    {
        var logger = Substitute.For<ILogger>();
        var provider = new EnvironmentVariableProvider(logger);

        var act = async () => await provider.DeleteSecretAsync("SOME_KEY");

        await act.Should().NotThrowAsync();
    }
}
