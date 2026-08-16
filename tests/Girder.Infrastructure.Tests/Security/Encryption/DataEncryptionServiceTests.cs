using Girder.Abstractions.Security.Encryption;
using Girder.Redis.Security.Encryption;
using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class DataEncryptionServiceTests
{
    private readonly DataEncryptionService _sut;
    private readonly IKeyManagementService _keyManagement = Substitute.For<IKeyManagementService>();
    private readonly ILogger<DataEncryptionService> _logger = Substitute.For<ILogger<DataEncryptionService>>();
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();

    public DataEncryptionServiceTests()
    {
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        var options = Options.Create(new DataEncryptionOptions());
        _sut = new DataEncryptionService(_keyManagement, _logger, options, _connectionMultiplexer);
    }

    #region EncryptAsync

    [Fact]
    public async Task EncryptAsync_NullData_ReturnsFailure()
    {
        var context = new EncryptionContext();

        var result = await _sut.EncryptAsync(null!, context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null or empty");
    }

    [Fact]
    public async Task EncryptAsync_EmptyData_ReturnsFailure()
    {
        var context = new EncryptionContext();

        var result = await _sut.EncryptAsync("", context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null or empty");
    }

    [Fact]
    public async Task EncryptAsync_NoSuitableKey_ReturnsFailure()
    {
        var context = new EncryptionContext();
        _keyManagement.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata>());

        var result = await _sut.EncryptAsync("test data", context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No suitable encryption key");
    }

    #endregion

    #region DecryptAsync

    [Fact]
    public async Task DecryptAsync_NullData_ReturnsFailure()
    {
        var context = new EncryptionContext();

        var result = await _sut.DecryptAsync(null!, context);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("null or empty");
    }

    [Fact]
    public async Task DecryptAsync_EmptyData_ReturnsFailure()
    {
        var context = new EncryptionContext();

        var result = await _sut.DecryptAsync("", context);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task DecryptAsync_InvalidBase64Data_ReturnsFailure()
    {
        var context = new EncryptionContext();

        var result = await _sut.DecryptAsync("not-valid-base64!!", context);

        result.Success.Should().BeFalse();
    }

    #endregion

    #region EncryptWithKeyAsync

    [Fact]
    public async Task EncryptWithKeyAsync_NullKey_ReturnsFailure()
    {
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns((EncryptionKey?)null);

        var result = await _sut.EncryptWithKeyAsync("data", "key-1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found or invalid");
    }

    [Fact]
    public async Task EncryptWithKeyAsync_InvalidKey_ReturnsFailure()
    {
        var invalidKey = new EncryptionKey
        {
            Id = "key-1",
            Status = KeyStatus.Disabled,
            KeyMaterial = new byte[32]
        };
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(invalidKey);

        var result = await _sut.EncryptWithKeyAsync("data", "key-1");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EncryptWithKeyAsync_ValidKey_ReturnsSuccess()
    {
        var key = CreateValidEncryptionKey("key-1");
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(key);

        var result = await _sut.EncryptWithKeyAsync("test data", "key-1");

        result.Success.Should().BeTrue();
        result.EncryptedData.Should().NotBeNullOrEmpty();
        result.KeyId.Should().Be("key-1");
    }

    [Fact]
    public async Task EncryptWithKeyAsync_AES128GCM_ReturnsSuccess()
    {
        var key = CreateValidEncryptionKey("key-128", keySize: 128);
        _keyManagement.GetKeyAsync("key-128", Arg.Any<CancellationToken>())
            .Returns(key);

        var options = new EncryptionOptions { Algorithm = EncryptionAlgorithm.AES128GCM };

        var result = await _sut.EncryptWithKeyAsync("test data", "key-128", options);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task EncryptWithKeyAsync_ChaCha20Poly1305_ReturnsSuccess()
    {
        var key = CreateValidEncryptionKey("key-chacha");
        _keyManagement.GetKeyAsync("key-chacha", Arg.Any<CancellationToken>())
            .Returns(key);

        var options = new EncryptionOptions { Algorithm = EncryptionAlgorithm.ChaCha20Poly1305 };

        var result = await _sut.EncryptWithKeyAsync("test data", "key-chacha", options);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task EncryptWithKeyAsync_WithCompression_SetsMetadata()
    {
        var key = CreateValidEncryptionKey("key-compress");
        _keyManagement.GetKeyAsync("key-compress", Arg.Any<CancellationToken>())
            .Returns(key);

        var options = new EncryptionOptions { CompressBeforeEncryption = true };

        var result = await _sut.EncryptWithKeyAsync("test data", "key-compress", options);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region DecryptWithKeyAsync

    [Fact]
    public async Task DecryptWithKeyAsync_NullKey_ReturnsFailure()
    {
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns((EncryptionKey?)null);

        var result = await _sut.DecryptWithKeyAsync("data", "key-1");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found or invalid");
    }

    [Fact]
    public async Task DecryptWithKeyAsync_InvalidEncryptedDataFormat_ReturnsFailure()
    {
        var key = CreateValidEncryptionKey("key-1");
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(key);

        var result = await _sut.DecryptWithKeyAsync("invalid-data", "key-1");

        result.Success.Should().BeFalse();
    }

    #endregion

    #region HashAsync

    [Fact]
    public async Task HashAsync_DefaultOptions_ReturnsSuccess()
    {
        var result = await _sut.HashAsync("password123");

        result.Success.Should().BeTrue();
        result.Hash.Should().NotBeNullOrEmpty();
        result.Salt.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(HashingAlgorithm.Argon2id)]
    [InlineData(HashingAlgorithm.Argon2i)]
    [InlineData(HashingAlgorithm.Argon2d)]
    [InlineData(HashingAlgorithm.BCrypt)]
    [InlineData(HashingAlgorithm.SCrypt)]
    public async Task HashAsync_UnimplementedAlgorithm_IsRefusedRatherThanSubstituted(
        HashingAlgorithm algorithm)
    {
        // Whoever asks for a memory-hard function must not silently receive
        // PBKDF2, which is the construction those functions exist to replace.
        // The service reports failures as results rather than exceptions, so
        // what matters is that nothing is hashed and the reason is named.
        var options = new HashingOptions { Algorithm = algorithm };

        var result = await _sut.HashAsync("password123", options);

        result.Success.Should().BeFalse();
        result.Hash.Should().BeNullOrEmpty();
        result.ErrorMessage.Should().Contain("not implemented");
    }

    [Fact]
    public async Task HashAsync_DefaultAlgorithm_IsOneThatExists()
    {
        var result = await _sut.HashAsync("password123");

        result.Algorithm.Should().Be(HashingAlgorithm.PBKDF2);
    }

    [Fact]
    public async Task HashAsync_PBKDF2_ReturnsHash()
    {
        var options = new HashingOptions { Algorithm = HashingAlgorithm.PBKDF2 };

        var result = await _sut.HashAsync("password123", options);

        result.Success.Should().BeTrue();
        result.Algorithm.Should().Be(HashingAlgorithm.PBKDF2);
    }

    [Fact]
    public async Task HashAsync_SHA256_ReturnsHash()
    {
        var options = new HashingOptions { Algorithm = HashingAlgorithm.SHA256 };

        var result = await _sut.HashAsync("password123", options);

        result.Success.Should().BeTrue();
        result.Algorithm.Should().Be(HashingAlgorithm.SHA256);
    }

    [Fact]
    public async Task HashAsync_SHA512_ReturnsHash()
    {
        var options = new HashingOptions { Algorithm = HashingAlgorithm.SHA512 };

        var result = await _sut.HashAsync("password123", options);

        result.Success.Should().BeTrue();
        result.Algorithm.Should().Be(HashingAlgorithm.SHA512);
    }

    [Fact]
    public async Task HashAsync_WithPepper_ProducesDifferentHash()
    {
        var options1 = new HashingOptions { Algorithm = HashingAlgorithm.SHA256, Pepper = "pepper1" };
        var options2 = new HashingOptions { Algorithm = HashingAlgorithm.SHA256, Pepper = "pepper2" };

        var result1 = await _sut.HashAsync("password123", options1);
        var result2 = await _sut.HashAsync("password123", options2);

        result1.Hash.Should().NotBe(result2.Hash);
    }

    [Fact]
    public async Task HashAsync_DifferentSalts_ProducesDifferentHashes()
    {
        var result1 = await _sut.HashAsync("password123");
        var result2 = await _sut.HashAsync("password123");

        result1.Salt.Should().NotBe(result2.Salt);
        result1.Hash.Should().NotBe(result2.Hash);
    }

    #endregion

    #region VerifyHashAsync

    [Fact]
    public async Task VerifyHashAsync_InvalidHashedData_ReturnsFalse()
    {
        var result = await _sut.VerifyHashAsync("test", "not-valid-json");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyHashAsync_NullHashedData_ReturnsFalse()
    {
        var result = await _sut.VerifyHashAsync("test", null!);

        result.Should().BeFalse();
    }

    #endregion

    #region GenerateKeyAsync

    [Fact]
    public async Task GenerateKeyAsync_Success_ReturnsKeyId()
    {
        var keyId = "new-key-1";
        _keyManagement.CreateKey(
            Arg.Any<KeyType>(), Arg.Any<KeyPurpose>(),
            Arg.Any<KeyGenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns(keyId);
        _keyManagement.GetKeyAsync(keyId, Arg.Any<CancellationToken>())
            .Returns(CreateValidEncryptionKey(keyId));

        var result = await _sut.GenerateKeyAsync(KeyType.Symmetric);

        result.Success.Should().BeTrue();
        result.KeyId.Should().Be(keyId);
        result.KeyType.Should().Be(KeyType.Symmetric);
    }

    [Fact]
    public async Task GenerateKeyAsync_GetKeyReturnsNull_ReturnsFailure()
    {
        _keyManagement.CreateKey(
            Arg.Any<KeyType>(), Arg.Any<KeyPurpose>(),
            Arg.Any<KeyGenerationOptions>(), Arg.Any<CancellationToken>())
            .Returns("key-1");
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns((EncryptionKey?)null);

        var result = await _sut.GenerateKeyAsync(KeyType.Symmetric);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateKeyAsync_Exception_ReturnsFailure()
    {
        _keyManagement.CreateKey(
            Arg.Any<KeyType>(), Arg.Any<KeyPurpose>(),
            Arg.Any<KeyGenerationOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Key creation failed"));

        var result = await _sut.GenerateKeyAsync(KeyType.Symmetric);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Key generation failed");
    }

    #endregion

    #region RotateKeyAsync

    [Fact]
    public async Task RotateKeyAsync_Success_ReturnsNewKeyId()
    {
        _keyManagement.RotateKeyAsync("old-key", Arg.Any<CancellationToken>())
            .Returns("new-key");
        _keyManagement.GetKeyAsync("new-key", Arg.Any<CancellationToken>())
            .Returns(CreateValidEncryptionKey("new-key"));

        var result = await _sut.RotateKeyAsync("old-key");

        result.Success.Should().BeTrue();
        result.OldKeyId.Should().Be("old-key");
        result.NewKeyId.Should().Be("new-key");
    }

    [Fact]
    public async Task RotateKeyAsync_Exception_ReturnsFailure()
    {
        _keyManagement.RotateKeyAsync("key-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Rotation failed"));

        var result = await _sut.RotateKeyAsync("key-1");

        result.Success.Should().BeFalse();
        result.OldKeyId.Should().Be("key-1");
    }

    #endregion

    #region GetKeyMetadataAsync

    [Fact]
    public async Task GetKeyMetadataAsync_KeyExists_ReturnsMetadata()
    {
        var key = CreateValidEncryptionKey("key-1");
        key.BackupInfo = new KeyBackupInfo { BackupId = "backup-1" };
        key.RotationSchedule = new KeyRotationSchedule { NextRotation = DateTime.UtcNow.AddDays(30) };
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(key);

        var result = await _sut.GetKeyMetadataAsync("key-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("key-1");
        result.HasBackup.Should().BeTrue();
    }

    [Fact]
    public async Task GetKeyMetadataAsync_KeyNotFound_ReturnsNull()
    {
        _keyManagement.GetKeyAsync("missing-key", Arg.Any<CancellationToken>())
            .Returns((EncryptionKey?)null);

        var result = await _sut.GetKeyMetadataAsync("missing-key");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetKeyMetadataAsync_Exception_ReturnsNull()
    {
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("DB error"));

        var result = await _sut.GetKeyMetadataAsync("key-1");

        result.Should().BeNull();
    }

    #endregion

    #region ReEncryptAsync

    [Fact]
    public async Task ReEncryptAsync_DecryptionFails_ReturnsFailure()
    {
        // Invalid key for decryption
        _keyManagement.GetKeyAsync("old-key", Arg.Any<CancellationToken>())
            .Returns((EncryptionKey?)null);

        var result = await _sut.ReEncryptAsync("encrypted-data", "old-key", "new-key");

        result.Success.Should().BeFalse();
    }

    #endregion

    #region SecureDeleteAsync

    [Fact]
    public async Task SecureDeleteAsync_DisablesKeyAndClearsCache()
    {
        await _sut.SecureDeleteAsync("key-1");

        await _keyManagement.Received(1).DisableKeyAsync("key-1", Arg.Any<CancellationToken>());
        await _database.Received(1).KeyDeleteAsync(Arg.Is<RedisKey>(k => k.ToString().Contains("key-1")));
    }

    [Fact]
    public async Task SecureDeleteAsync_DisableThrows_Propagates()
    {
        _keyManagement.DisableKeyAsync("key-1", Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Cannot disable"));

        var action = () => _sut.SecureDeleteAsync("key-1");

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    private static EncryptionKey CreateValidEncryptionKey(string keyId, int keySize = 256)
    {
        var keyMaterial = new byte[keySize / 8];
        Random.Shared.NextBytes(keyMaterial);

        return new EncryptionKey
        {
            Id = keyId,
            Status = KeyStatus.Active,
            KeySize = keySize,
            KeyMaterial = keyMaterial,
            KeyType = KeyType.Symmetric,
            Purpose = KeyPurpose.DataEncryption,
            Version = 1,
            ExpiresAt = DateTime.UtcNow.AddYears(1),
            UsageStatistics = new KeyUsageStatistics()
        };
    }
}
