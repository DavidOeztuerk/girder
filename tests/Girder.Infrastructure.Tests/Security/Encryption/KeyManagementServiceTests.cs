using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class KeyManagementServiceTests
{
    private readonly KeyManagementService _sut;
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly ILogger<KeyManagementService> _logger = Substitute.For<ILogger<KeyManagementService>>();

    public KeyManagementServiceTests()
    {
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        var options = Options.Create(new KeyManagementOptions
        {
            AutoRotateKeys = false,
            DefaultRotationInterval = TimeSpan.FromDays(90)
        });
        _sut = new KeyManagementService(_connectionMultiplexer, _logger, options);
    }

    #region CreateKey

    [Fact]
    public void CreateKey_Symmetric256_ReturnsKeyId()
    {
        var keyId = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        keyId.Should().NotBeNullOrEmpty();
        keyId.Should().StartWith("key_");
    }

    [Fact]
    public void CreateKey_StoresInRedis()
    {
        _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        _database.Received(1).StringSet(
            Arg.Is<RedisKey>(k => k.ToString().Contains("keys:data:key_")),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public void CreateKey_AddsToActiveKeysIndex()
    {
        _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        _database.Received(1).SetAdd(
            Arg.Is<RedisKey>(k => k.ToString().Contains("keys:active:DataEncryption")),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public void CreateKey_WithRotationInterval_ReturnsKeyId()
    {
        var options = new KeyGenerationOptions
        {
            Purpose = KeyPurpose.DataEncryption,
            RotationInterval = TimeSpan.FromDays(30)
        };

        var keyId = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption, options);

        keyId.Should().NotBeNullOrEmpty();
        keyId.Should().StartWith("key_");
    }

    [Fact]
    public void CreateKey_WithExpiresAt_ReturnsKeyId()
    {
        var options = new KeyGenerationOptions
        {
            Purpose = KeyPurpose.DataEncryption,
            ExpiresAt = DateTime.UtcNow.AddDays(365)
        };

        var keyId = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption, options);

        keyId.Should().NotBeNullOrEmpty();
        keyId.Should().StartWith("key_");
    }

    [Fact]
    public void CreateKey_RedisError_Throws()
    {
        _database.When(x => x.StringSet(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(), Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(), Arg.Any<CommandFlags>()))
            .Throw(new RedisException("fail"));

        var act = () => _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        act.Should().Throw<RedisException>();
    }

    #endregion

    #region GetKeyAsync

    [Fact]
    public async Task GetKeyAsync_KeyNotFound_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetKeyAsync("non-existent-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetKeyAsync_RedisError_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.GetKeyAsync("some-key");

        result.Should().BeNull();
    }

    #endregion

    #region RotateKeyAsync

    [Fact]
    public async Task RotateKeyAsync_KeyNotFound_ThrowsInvalidOperation()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var act = () => _sut.RotateKeyAsync("non-existent");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region DisableKeyAsync

    [Fact]
    public async Task DisableKeyAsync_KeyNotFound_DoesNotThrow()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var act = () => _sut.DisableKeyAsync("non-existent");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region GetActiveKeysAsync

    [Fact]
    public async Task GetActiveKeysAsync_NoKeys_ReturnsEmpty()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.GetActiveKeysAsync(KeyPurpose.DataEncryption);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveKeysAsync_RedisError_ReturnsEmpty()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.GetActiveKeysAsync(KeyPurpose.DataEncryption);

        result.Should().BeEmpty();
    }

    #endregion

    #region GetKeyUsageAsync

    [Fact]
    public async Task GetKeyUsageAsync_KeyNotFound_ReturnsDefaultStats()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetKeyUsageAsync("non-existent");

        result.Should().NotBeNull();
        result.EncryptionOperations.Should().Be(0);
    }

    [Fact]
    public async Task GetKeyUsageAsync_RedisError_ReturnsDefaultStats()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("fail"));

        var result = await _sut.GetKeyUsageAsync("some-key");

        result.Should().NotBeNull();
    }

    #endregion

    #region ScheduleKeyRotationAsync

    [Fact]
    public async Task ScheduleKeyRotationAsync_KeyNotFound_Throws()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var act = () => _sut.ScheduleKeyRotationAsync("non-existent", DateTime.UtcNow.AddDays(30));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region BackupKeyAsync

    [Fact]
    public async Task BackupKeyAsync_KeyNotFound_ReturnsFailure()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.BackupKeyAsync("non-existent");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    #endregion

    #region RestoreKeyAsync

    [Fact]
    public async Task RestoreKeyAsync_BackupNotFound_Throws()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var act = () => _sut.RestoreKeyAsync("non-existent-backup");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Additional Tests

    [Fact]
    public async Task GetActiveKeysAsync_WithKeyIds_CallsGetKeyForEachId()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "key_abc123" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("{\"id\":\"key_abc123\"}"));

        var result = await _sut.GetActiveKeysAsync(KeyPurpose.DataEncryption);

        result.Should().NotBeNull();
        await _database.Received(1).StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetActiveKeysAsync_WithInvalidJson_SkipsKey()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "bad-key-id" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("{ invalid json }"));

        var result = await _sut.GetActiveKeysAsync(KeyPurpose.DataEncryption);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetActiveKeysAsync_WithMultipleKeyIds_QueriesEachKey()
    {
        var keyId1 = "key_one";
        var keyId2 = "key_two";

        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { keyId1, keyId2 });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(callInfo =>
            {
                var key = callInfo.ArgAt<RedisKey>(0).ToString();
                if (key.Contains(keyId1)) return new RedisValue("{\"id\":\"key_one\"}");
                if (key.Contains(keyId2)) return new RedisValue("{\"id\":\"key_two\"}");
                return RedisValue.Null;
            });

        var result = await _sut.GetActiveKeysAsync(KeyPurpose.DataEncryption);

        result.Should().NotBeNull();
        await _database.Received(2).StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task GetActiveKeysAsync_KeyNotFoundInStorage_SkipsKey()
    {
        _database.SetMembersAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { "missing-key-id" });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetActiveKeysAsync(KeyPurpose.DataEncryption);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task RotateKeyAsync_ExistingKey_CreatesNewKeyVersion()
    {
        var existingKey = CreateEncryptionKey("key_existing", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var newKeyId = await _sut.RotateKeyAsync("key_existing");

        newKeyId.Should().NotBeNullOrEmpty();
        newKeyId.Should().StartWith("key_");
    }

    [Fact]
    public async Task RotateKeyAsync_ExistingKey_MarksOldKeyAsRotated()
    {
        var existingKey = CreateEncryptionKey("key_rotate_me", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        await _sut.RotateKeyAsync("key_rotate_me");

        await _database.Received().StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DisableKeyAsync_ExistingKey_DisablesIt()
    {
        var existingKey = CreateEncryptionKey("key_to_disable", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        await _sut.DisableKeyAsync("key_to_disable");

        await _database.Received().StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DisableKeyAsync_ExistingKey_RemovesFromActiveIndex()
    {
        var existingKey = CreateEncryptionKey("key_disable_2", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        await _sut.DisableKeyAsync("key_disable_2");

        await _database.Received(1).SetRemoveAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("active")),
            Arg.Any<RedisValue>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task BackupKeyAsync_ExistingKey_ReturnsSuccess()
    {
        var existingKey = CreateEncryptionKey("key_to_backup", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.BackupKeyAsync("key_to_backup");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task BackupKeyAsync_ExistingKey_StoresBackup()
    {
        var existingKey = CreateEncryptionKey("key_backup_store", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        await _sut.BackupKeyAsync("key_backup_store");

        await _database.Received().StringSetAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains("backup")),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task BackupKeyAsync_ExistingKey_ReturnsBackupId()
    {
        var existingKey = CreateEncryptionKey("key_backup_id", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.BackupKeyAsync("key_backup_id");

        result.BackupId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RestoreKeyAsync_ExistingBackup_RestoresKey()
    {
        var backupData = new
        {
            KeyId = "key-original",
            KeyMaterial = Convert.ToBase64String(new byte[32]),
            KeyType = "Symmetric",
            Purpose = "DataEncryption",
            KeySize = 256,
            CreatedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>(),
            BackupTimestamp = DateTime.UtcNow
        };
        var backupJson = JsonSerializer.Serialize(backupData);
        var encryptedBackup = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(backupJson));

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(encryptedBackup));

        var result = await _sut.RestoreKeyAsync("backup-123");

        result.Should().NotBeNullOrEmpty();
        result.Should().StartWith("key_");
    }

    [Fact]
    public async Task ScheduleKeyRotationAsync_ExistingKey_SetsRotationDate()
    {
        var existingKey = CreateEncryptionKey("key_schedule", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var scheduledDate = DateTime.UtcNow.AddDays(30);
        await _sut.ScheduleKeyRotationAsync("key_schedule", scheduledDate);

        await _database.Received().StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task ScheduleKeyRotationAsync_ExistingKey_DoesNotThrow()
    {
        var existingKey = CreateEncryptionKey("key_schedule_2", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var act = () => _sut.ScheduleKeyRotationAsync("key_schedule_2", DateTime.UtcNow.AddDays(90));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetKeyUsageAsync_ExistingKey_ReturnsStats()
    {
        var existingKey = CreateEncryptionKey("key_usage", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.GetKeyUsageAsync("key_usage");

        result.Should().NotBeNull();
        result.EncryptionOperations.Should().BeGreaterThanOrEqualTo(0);
        result.DecryptionOperations.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetKeyAsync_ExistingKey_ReturnsNonNull()
    {
        var existingKey = CreateEncryptionKey("key_get_test", KeyPurpose.DataEncryption);
        var json = JsonSerializer.Serialize(existingKey);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.GetKeyAsync("key_get_test");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetKeyAsync_InvalidJson_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("{ bad json }"));

        var result = await _sut.GetKeyAsync("key_bad_json");

        result.Should().BeNull();
    }

    [Fact]
    public void CreateKey_AsymmetricRSA_ReturnsKeyId()
    {
        var keyId = _sut.CreateKey(KeyType.Asymmetric, KeyPurpose.Signing);

        keyId.Should().NotBeNullOrEmpty();
        keyId.Should().StartWith("key_");
    }

    [Fact]
    public void CreateKey_DifferentPurposes_AllReturn()
    {
        var dataKey = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
        var signingKey = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.Signing);
        var authKey = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.Authentication);

        dataKey.Should().NotBeNullOrEmpty();
        signingKey.Should().NotBeNullOrEmpty();
        authKey.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CreateKey_TwoCalls_ReturnDifferentIds()
    {
        var key1 = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);
        var key2 = _sut.CreateKey(KeyType.Symmetric, KeyPurpose.DataEncryption);

        key1.Should().NotBe(key2);
    }

    private static EncryptionKey CreateEncryptionKey(string id, KeyPurpose purpose)
    {
        return new EncryptionKey
        {
            Id = id,
            Status = KeyStatus.Active,
            KeyMaterial = new byte[32],
            KeySize = 256,
            KeyType = KeyType.Symmetric,
            Purpose = purpose,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(90),
            UsageStatistics = new KeyUsageStatistics()
        };
    }

    #endregion
}
