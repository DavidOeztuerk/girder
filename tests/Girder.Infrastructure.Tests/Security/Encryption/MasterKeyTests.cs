using System.Text;
using System.Text.Json;
using Girder.Infrastructure.Security.Encryption;
using Girder.Infrastructure.Security.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class ConfiguredMasterKeyProviderTests
{
    private static IConfiguration ConfigurationWith(string? value) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfiguredMasterKeyProvider.ConfigurationKey] = value
            })
            .Build();

    [Fact]
    public void WithoutAKey_StartupFails()
    {
        // Girder generates no master key. A key the application invents is a
        // key the operator does not hold.
        var act = () => new ConfiguredMasterKeyProvider(ConfigurationWith(null));

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not generate*");
    }

    [Fact]
    public void AKeyThatIsNotBase64_IsRejected()
    {
        var act = () => new ConfiguredMasterKeyProvider(ConfigurationWith("not base64!!"));

        act.Should().Throw<InvalidOperationException>().WithMessage("*base64*");
    }

    [Fact]
    public void AKeyOfTheWrongLength_IsRejected()
    {
        var act = () => new ConfiguredMasterKeyProvider(
            ConfigurationWith(Convert.ToBase64String(new byte[16])));

        act.Should().Throw<InvalidOperationException>().WithMessage("*16 bytes*32*");
    }

    [Fact]
    public void AValidKeyIsReturnedUnchanged()
    {
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

        var provider = new ConfiguredMasterKeyProvider(ConfigurationWith(Convert.ToBase64String(key)));

        provider.GetMasterKey().Should().Equal(key);
    }
}

[Trait("Category", "Unit")]
public class SecretStoreMasterKeyProviderTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void TheKeyComesFromTheSecretStore()
    {
        var store = Substitute.For<ISecretProvider>();
        store.GetSecretAsync("girder/master-key", Arg.Any<CancellationToken>())
            .Returns(Convert.ToBase64String(Key));

        var provider = new SecretStoreMasterKeyProvider(
            store, NullLogger<SecretStoreMasterKeyProvider>.Instance);

        provider.GetMasterKey().Should().Equal(Key);
    }

    [Fact]
    public void AnEmptySecretIsRefusedRatherThanGenerated()
    {
        var store = Substitute.For<ISecretProvider>();
        store.GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var provider = new SecretStoreMasterKeyProvider(
            store, NullLogger<SecretStoreMasterKeyProvider>.Instance);

        var act = () => provider.GetMasterKey();

        act.Should().Throw<InvalidOperationException>().WithMessage("*will not generate one*");
    }

    [Fact]
    public void TheStoreIsAskedOnceAndTheAnswerIsKept()
    {
        var store = Substitute.For<ISecretProvider>();
        store.GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Convert.ToBase64String(Key));

        var provider = new SecretStoreMasterKeyProvider(
            store, NullLogger<SecretStoreMasterKeyProvider>.Instance);

        provider.GetMasterKey();
        provider.GetMasterKey();
        provider.GetMasterKey();

        store.Received(1).GetSecretAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ADifferentSecretNameIsHonoured()
    {
        var store = Substitute.For<ISecretProvider>();
        store.GetSecretAsync("tenant-a/master-key", Arg.Any<CancellationToken>())
            .Returns(Convert.ToBase64String(Key));

        var provider = new SecretStoreMasterKeyProvider(
            store, NullLogger<SecretStoreMasterKeyProvider>.Instance, "tenant-a/master-key");

        provider.GetMasterKey().Should().Equal(Key);
    }
}

/// <summary>
/// What actually reaches the store. These are the regression tests for a
/// version in which key material was written out as base64 — an encoding, not a
/// cipher — behind a comment claiming it was encrypted.
/// </summary>
[Trait("Category", "Unit")]
public class StoredKeyMaterialTests
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly byte[] _masterKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private KeyManagementService CreateService(byte[]? masterKey = null)
    {
        var multiplexer = Substitute.For<IConnectionMultiplexer>();
        multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);

        return new KeyManagementService(
            multiplexer,
            NullLogger<KeyManagementService>.Instance,
            Options.Create(new KeyManagementOptions()),
            new FixedMasterKey(masterKey ?? _masterKey));
    }

    private string CapturedStoredValue()
    {
        var call = _database.ReceivedCalls()
            .First(c => c.GetMethodInfo().Name == nameof(IDatabase.StringSet));

        return call.GetArguments()[1]!.ToString()!;
    }

    [Fact]
    public void KeyMaterialNeverReachesTheStoreInTheClear()
    {
        var service = CreateService();

        service.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        var stored = CapturedStoredValue();
        var material = JsonDocument.Parse(stored).RootElement
            .GetProperty(nameof(EncryptionKey.KeyMaterial)).GetString()!;
        var sealedBytes = Convert.FromBase64String(material);

        // A sealed 256-bit key is nonce + tag + ciphertext, so it is longer than
        // the key itself. Equal length would mean the material was merely encoded.
        sealedBytes.Length.Should().Be(12 + 16 + 32);
    }

    [Fact]
    public async Task AStoredKeyComesBackWithItsMaterial()
    {
        var service = CreateService();
        service.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
        var stored = CapturedStoredValue();

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(stored));

        var key = await service.GetKeyAsync("any-id");

        key.Should().NotBeNull();
        key!.KeyMaterial.Should().HaveCount(32);
        key.IsValid().Should().BeTrue();
    }

    [Fact]
    public async Task AnotherMasterKeyCannotOpenIt()
    {
        var service = CreateService();
        service.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
        var stored = CapturedStoredValue();

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(stored));

        var intruder = CreateService(Enumerable.Repeat((byte)0xFF, 32).ToArray());

        // The service reports a failed read as null rather than throwing.
        (await intruder.GetKeyAsync("any-id")).Should().BeNull();
    }

    [Fact]
    public async Task TamperedMaterialIsRejected()
    {
        var service = CreateService();
        service.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        var node = System.Text.Json.Nodes.JsonNode.Parse(CapturedStoredValue())!.AsObject();
        var material = Convert.FromBase64String(node[nameof(EncryptionKey.KeyMaterial)]!.GetValue<string>());
        material[^1] ^= 0xFF;
        node[nameof(EncryptionKey.KeyMaterial)] = Convert.ToBase64String(material);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(node.ToJsonString()));

        // AES-GCM authenticates, so a changed byte fails to open instead of
        // decrypting into something the caller then uses as a key.
        (await service.GetKeyAsync("any-id")).Should().BeNull();
    }

    private sealed class FixedMasterKey(byte[] key) : IMasterKeyProvider
    {
        public byte[] GetMasterKey() => key;
    }
}
