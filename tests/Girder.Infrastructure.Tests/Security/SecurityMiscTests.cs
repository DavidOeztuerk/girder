using Girder.InMemory.Security;
using Girder.Infrastructure.Security;

namespace Girder.Infrastructure.Tests.Security;

[Trait("Category", "Unit")]
public class SecretManagerModelsTests
{
    [Fact]
    public void InMemorySecretManager_CanBeConstructed()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        manager.Should().NotBeNull();
    }

    [Fact]
    public async Task InMemorySecretManager_SetAndGet_ReturnsValue()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("key", "value");
        var result = await manager.GetSecretAsync("key");

        result.Should().Be("value");
    }

    [Fact]
    public async Task InMemorySecretManager_GetNonExistent_ReturnsNull()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        var result = await manager.GetSecretAsync("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public async Task InMemorySecretManager_Delete_RemovesSecret()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("to-delete", "value");
        await manager.DeleteSecretAsync("to-delete");

        var result = await manager.GetSecretAsync("to-delete");
        result.Should().BeNull();
    }

    [Fact]
    public async Task InMemorySecretManager_Exists_ReturnsTrueForExistingSecret()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("exists", "value");
        var result = await manager.SecretExistsAsync("exists");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task InMemorySecretManager_Exists_ReturnsFalseForMissingSecret()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        var result = await manager.SecretExistsAsync("missing");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task InMemorySecretManager_Rotate_CreatesNewValue()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("rotatable", "original");
        var newValue = await manager.RotateSecretAsync("rotatable");

        newValue.Should().NotBeNullOrEmpty();
        newValue.Should().NotBe("original");

        var current = await manager.GetSecretAsync("rotatable");
        current.Should().Be(newValue);
    }

    [Fact]
    public async Task InMemorySecretManager_GetNames_ReturnsAllNames()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("secret-a", "val-a");
        await manager.SetSecretAsync("secret-b", "val-b");

        var names = (await manager.GetSecretNamesAsync()).ToList();

        names.Should().Contain("secret-a");
        names.Should().Contain("secret-b");
    }

    [Fact]
    public async Task InMemorySecretManager_GetHistory_ReturnsVersionsOrderedByVersionDesc()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("versioned", "v1");
        await manager.SetSecretAsync("versioned", "v2");
        await manager.SetSecretAsync("versioned", "v3");

        var history = (await manager.GetSecretHistoryAsync("versioned")).ToList();

        history.Should().HaveCount(3);
        history.First().Version.Should().BeGreaterThan(history.Last().Version);
    }

    [Fact]
    public async Task InMemorySecretManager_GetHistory_NonExistent_ReturnsEmpty()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        var history = await manager.GetSecretHistoryAsync("none");

        history.Should().BeEmpty();
    }

    [Fact]
    public async Task InMemorySecretManager_MultipleVersions_OnlyActiveIsReturned()
    {
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InMemorySecretManager>>();
        var manager = new InMemorySecretManager(logger);

        await manager.SetSecretAsync("versioned", "old-value");
        await manager.SetSecretAsync("versioned", "new-value");

        var result = await manager.GetSecretAsync("versioned");
        result.Should().Be("new-value");

        var history = (await manager.GetSecretHistoryAsync("versioned")).ToList();
        history.Count(v => v.IsActive).Should().Be(1);
    }
}
