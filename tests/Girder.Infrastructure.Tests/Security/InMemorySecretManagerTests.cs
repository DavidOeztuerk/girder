using Girder.InMemory.Security;
using Girder.Infrastructure.Security;
using Microsoft.Extensions.Logging;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class InMemorySecretManagerTests
{
    private readonly InMemorySecretManager _manager;

    public InMemorySecretManagerTests()
    {
        var logger = Substitute.For<ILogger<InMemorySecretManager>>();
        _manager = new InMemorySecretManager(logger);
    }

    [Fact]
    public async Task SetSecretAsync_ThenGetSecretAsync_ReturnsValue()
    {
        await _manager.SetSecretAsync("my-secret", "my-value");

        var result = await _manager.GetSecretAsync("my-secret");

        result.Should().Be("my-value");
    }

    [Fact]
    public async Task GetSecretAsync_NonExistentSecret_ReturnsNull()
    {
        var result = await _manager.GetSecretAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task SetSecretAsync_MultipleTimes_DeactivatesPreviousVersions()
    {
        await _manager.SetSecretAsync("my-secret", "value1");
        await _manager.SetSecretAsync("my-secret", "value2");

        var result = await _manager.GetSecretAsync("my-secret");
        result.Should().Be("value2");

        var history = (await _manager.GetSecretHistoryAsync("my-secret")).ToList();
        history.Should().HaveCount(2);
        history.Count(v => v.IsActive).Should().Be(1);
    }

    [Fact]
    public async Task SecretExistsAsync_ExistingSecret_ReturnsTrue()
    {
        await _manager.SetSecretAsync("test", "val");

        var result = await _manager.SecretExistsAsync("test");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_NonExistentSecret_ReturnsFalse()
    {
        var result = await _manager.SecretExistsAsync("nope");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_RemovesSecret()
    {
        await _manager.SetSecretAsync("to-delete", "val");
        await _manager.DeleteSecretAsync("to-delete");

        var exists = await _manager.SecretExistsAsync("to-delete");
        exists.Should().BeFalse();

        var value = await _manager.GetSecretAsync("to-delete");
        value.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretNamesAsync_ReturnsAllNames()
    {
        await _manager.SetSecretAsync("secret1", "val1");
        await _manager.SetSecretAsync("secret2", "val2");

        var names = (await _manager.GetSecretNamesAsync()).ToList();

        names.Should().Contain("secret1");
        names.Should().Contain("secret2");
    }

    [Fact]
    public async Task RotateSecretAsync_CreatesNewVersion()
    {
        await _manager.SetSecretAsync("rotate-me", "original");

        var newValue = await _manager.RotateSecretAsync("rotate-me");

        newValue.Should().NotBeNullOrEmpty();
        newValue.Should().NotBe("original");

        var current = await _manager.GetSecretAsync("rotate-me");
        current.Should().Be(newValue);
    }

    [Fact]
    public async Task GetSecretHistoryAsync_NonExistentSecret_ReturnsEmpty()
    {
        var history = await _manager.GetSecretHistoryAsync("no-history");

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSecretHistoryAsync_ReturnsOrderedByVersionDescending()
    {
        await _manager.SetSecretAsync("versioned", "v1");
        await _manager.SetSecretAsync("versioned", "v2");
        await _manager.SetSecretAsync("versioned", "v3");

        var history = (await _manager.GetSecretHistoryAsync("versioned")).ToList();

        history.Should().HaveCount(3);
        history.First().Version.Should().BeGreaterThan(history.Last().Version);
    }
}
