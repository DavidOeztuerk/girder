using Girder.Infrastructure.Security.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security.Secrets;

// ─────────────────────────────────────────────────────────────────────────────
// FileBasedProvider
// NOTE: FileBasedProvider uses a static in-process dictionary as its primary
// store. The configured file path is used only for persistence (load/save).
// This means all FileBasedProvider instances within the same test process share
// the same key/value space. Tests must use globally unique key names and must
// NOT assert on the total number of keys or assume an empty initial state.
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Unit")]
public class FileBasedProviderTests
{
    private static FileBasedProvider CreateProvider()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"secrets_test_{Guid.NewGuid():N}.json");
        var logger = Substitute.For<ILogger>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // These are the keys the provider reads; the previous names
                // never reached it, so the tests ran on a built-in password.
                ["Secrets:FilePath"] = tempFile,
                ["Secrets:MasterPassword"] = "test-password-abc123"
            })
            .Build();
        return new FileBasedProvider(logger, config);
    }

    [Fact]
    public void Constructor_WithoutMasterPassword_Refuses()
    {
        // Encrypting under a password compiled into the library is not
        // encryption, so there is no fallback and startup fails instead.
        var logger = Substitute.For<ILogger>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var act = () => new FileBasedProvider(logger, config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Secrets:MasterPassword is required*");
    }

    [Fact]
    public async Task GetSecretAsync_KeyThatWasNeverSet_ReturnsNull()
    {
        var provider = CreateProvider();
        var uniqueKey = $"fb-get-never-set-{Guid.NewGuid():N}";

        var result = await provider.GetSecretAsync(uniqueKey);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSecretAsync_ThenGetSecretAsync_ReturnsStoredValue()
    {
        var provider = CreateProvider();
        var key = $"fb-set-get-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "my-value");
        var result = await provider.GetSecretAsync(key);

        result.Should().Be("my-value");
    }

    [Fact]
    public async Task SetSecretAsync_OverwriteExistingKey_ReturnsUpdatedValue()
    {
        var provider = CreateProvider();
        var key = $"fb-overwrite-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "original");
        await provider.SetSecretAsync(key, "updated");

        var result = await provider.GetSecretAsync(key);

        result.Should().Be("updated");
    }

    [Fact]
    public async Task DeleteSecretAsync_ExistingKey_RemovesIt()
    {
        var provider = CreateProvider();
        var key = $"fb-delete-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "value");
        await provider.DeleteSecretAsync(key);

        var result = await provider.GetSecretAsync(key);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteSecretAsync_NonExistentKey_DoesNotThrow()
    {
        var provider = CreateProvider();
        var key = $"fb-del-nonexist-{Guid.NewGuid():N}";

        var act = async () => await provider.DeleteSecretAsync(key);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SecretExistsAsync_KeyThatWasSet_ReturnsTrue()
    {
        var provider = CreateProvider();
        var key = $"fb-exists-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "value");
        var result = await provider.SecretExistsAsync(key);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_KeyThatWasNeverSet_ReturnsFalse()
    {
        var provider = CreateProvider();
        var key = $"fb-notexists-{Guid.NewGuid():N}";

        var result = await provider.SecretExistsAsync(key);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecretExistsAsync_KeyThatWasDeleted_ReturnsFalse()
    {
        var provider = CreateProvider();
        var key = $"fb-del-exists-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "value");
        await provider.DeleteSecretAsync(key);

        var result = await provider.SecretExistsAsync(key);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListSecretKeysAsync_ContainsRecentlySetKey()
    {
        var provider = CreateProvider();
        var key = $"fb-list-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "value");
        var keys = await provider.ListSecretKeysAsync();

        keys.Should().Contain(key);
    }

    [Fact]
    public async Task ListSecretKeysAsync_DoesNotContainDeletedKey()
    {
        var provider = CreateProvider();
        var key = $"fb-list-del-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "value");
        await provider.DeleteSecretAsync(key);

        var keys = await provider.ListSecretKeysAsync();

        keys.Should().NotContain(key);
    }

    [Fact]
    public async Task SetSecretAsync_MultipleDistinctKeys_AllRetrievable()
    {
        var provider = CreateProvider();
        var keyA = $"fb-multi-a-{Guid.NewGuid():N}";
        var keyB = $"fb-multi-b-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(keyA, "val-a");
        await provider.SetSecretAsync(keyB, "val-b");

        (await provider.GetSecretAsync(keyA)).Should().Be("val-a");
        (await provider.GetSecretAsync(keyB)).Should().Be("val-b");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DataProtectionSecretProvider
// ─────────────────────────────────────────────────────────────────────────────

[Trait("Category", "Unit")]
public class DataProtectionSecretProviderTests
{
    // Each test creates an isolated provider with its own DataProtection application name
    // so in-memory stores do not bleed across tests.
    private static DataProtectionSecretProvider CreateProvider()
    {
        var logger = Substitute.For<ILogger<DataProtectionSecretProvider>>();
        var dp = DataProtectionProvider.Create($"TestApp_{Guid.NewGuid():N}");
        return new DataProtectionSecretProvider(logger, dp);
    }

    [Fact]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        var provider = CreateProvider();

        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentKey_ReturnsNull()
    {
        var provider = CreateProvider();

        var result = await provider.GetSecretAsync("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSecretAsync_ThenGetSecretAsync_ReturnsDecryptedValue()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("my-key", "my-value");
        var result = await provider.GetSecretAsync("my-key");

        result.Should().Be("my-value");
    }

    [Fact]
    public async Task SetSecretAsync_OverwriteExistingKey_ReturnsUpdatedValue()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("key", "original-value");
        await provider.SetSecretAsync("key", "updated-value");

        var result = await provider.GetSecretAsync("key");

        result.Should().Be("updated-value");
    }

    [Fact]
    public async Task DeleteSecretAsync_ExistingKey_RemovesIt()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("delete-key", "delete-value");
        await provider.DeleteSecretAsync("delete-key");

        var result = await provider.GetSecretAsync("delete-key");

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
    public async Task SecretExistsAsync_BeforeSetting_ReturnsFalse()
    {
        var provider = CreateProvider();

        var result = await provider.SecretExistsAsync("not-set-yet");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecretExistsAsync_AfterSetting_ReturnsTrue()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("exists-key", "value");
        var result = await provider.SecretExistsAsync("exists-key");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_AfterDeleting_ReturnsFalse()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("temp-key", "temp-value");
        await provider.DeleteSecretAsync("temp-key");

        var result = await provider.SecretExistsAsync("temp-key");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListSecretKeysAsync_EmptyProvider_ReturnsEmptyCollection()
    {
        var provider = CreateProvider();

        var keys = await provider.ListSecretKeysAsync();

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSecretKeysAsync_AfterSettingKeys_ContainsAllKeys()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("list-key-one", "v1");
        await provider.SetSecretAsync("list-key-two", "v2");

        var keys = (await provider.ListSecretKeysAsync()).ToList();

        keys.Should().Contain("list-key-one");
        keys.Should().Contain("list-key-two");
    }

    [Fact]
    public async Task ListSecretKeysAsync_AfterDeletingKey_DoesNotContainDeletedKey()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("keep-key", "val1");
        await provider.SetSecretAsync("remove-key", "val2");
        await provider.DeleteSecretAsync("remove-key");

        var keys = (await provider.ListSecretKeysAsync()).ToList();

        keys.Should().Contain("keep-key");
        keys.Should().NotContain("remove-key");
    }

    [Fact]
    public async Task SetAndGet_SpecialCharacterValues_RoundTripsCorrectly()
    {
        var provider = CreateProvider();
        var specialValue = "value with spaces, newlines\nand \"quotes\" and unicode: 日本語";

        await provider.SetSecretAsync("special-key", specialValue);
        var result = await provider.GetSecretAsync("special-key");

        result.Should().Be(specialValue);
    }

    [Fact]
    public async Task SetAndGet_MultipleIndependentKeys_DoNotInterfere()
    {
        var provider = CreateProvider();

        await provider.SetSecretAsync("key-a", "value-a");
        await provider.SetSecretAsync("key-b", "value-b");
        await provider.SetSecretAsync("key-c", "value-c");

        (await provider.GetSecretAsync("key-a")).Should().Be("value-a");
        (await provider.GetSecretAsync("key-b")).Should().Be("value-b");
        (await provider.GetSecretAsync("key-c")).Should().Be("value-c");
    }
}

