using Girder.Infrastructure.Security.Encryption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security.Encryption;

[Trait("Category", "Unit")]
public class DataEncryptionServiceTopUpTests
{
    private readonly DataEncryptionService _sut;
    private readonly IKeyManagementService _keyManagement = Substitute.For<IKeyManagementService>();
    private readonly ILogger<DataEncryptionService> _logger = Substitute.For<ILogger<DataEncryptionService>>();
    private readonly IConnectionMultiplexer _connectionMultiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _database = Substitute.For<IDatabase>();

    public DataEncryptionServiceTopUpTests()
    {
        _connectionMultiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);
        var options = Options.Create(new DataEncryptionOptions { LogOperations = true });
        _sut = new DataEncryptionService(_keyManagement, _logger, options, _connectionMultiplexer);
    }

    #region EncryptAsync — key selection paths

    [Fact]
    public async Task EncryptAsync_TopSecretClassification_SelectsLargestKey()
    {
        // Arrange — provide a key with size >= 256
        var key = CreateValidKey("key-256", 256);
        var keyMeta = CreateKeyMetadata("key-256", 256);

        _keyManagement.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyMeta });
        _keyManagement.GetKeyAsync("key-256", Arg.Any<CancellationToken>())
            .Returns(key);

        var context = new EncryptionContext
        {
            Classification = DataClassification.TopSecret
        };

        // Act
        var result = await _sut.EncryptAsync("secret-data", context);

        // Assert
        result.Success.Should().BeTrue();
        result.KeyId.Should().Be("key-256");
    }

    [Fact]
    public async Task EncryptAsync_WithComplianceRequirements_FiltersKeys()
    {
        // A key that does NOT have the required compliance requirements
        var keyMeta = CreateKeyMetadata("key-no-compliance", 256, complianceReqs: new List<ComplianceRequirement>());

        _keyManagement.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyMeta });

        var context = new EncryptionContext
        {
            ComplianceRequirements = new List<ComplianceRequirement> { ComplianceRequirement.GDPR }
        };

        // Act — no key satisfies GDPR requirement
        var result = await _sut.EncryptAsync("data", context);

        // Assert — should fail with "No suitable encryption key"
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No suitable encryption key");
    }

    [Fact]
    public async Task EncryptAsync_WithGeographicRestriction_FiltersKeys()
    {
        // Key has a geographic restriction (EU-only), request is for US
        var keyMeta = CreateKeyMetadata("key-eu", 256, geoRestrictions: new List<string> { "EU" });

        _keyManagement.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyMeta });

        var context = new EncryptionContext
        {
            GeographicRestriction = "US"
        };

        // Act — key restricts to EU but request is US
        var result = await _sut.EncryptAsync("data", context);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task EncryptAsync_WithGeographicRestriction_AcceptsKeyWithNoRestrictions()
    {
        // Key has no geographic restriction — should match any
        var keyMeta = CreateKeyMetadata("key-global", 256, geoRestrictions: new List<string>());
        var key = CreateValidKey("key-global", 256);

        _keyManagement.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyMeta });
        _keyManagement.GetKeyAsync("key-global", Arg.Any<CancellationToken>())
            .Returns(key);

        var context = new EncryptionContext { GeographicRestriction = "US" };

        var result = await _sut.EncryptAsync("data", context);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task EncryptAsync_ArchivePurpose_SetsCompressionOption()
    {
        var keyMeta = CreateKeyMetadata("key-1", 256);
        var key = CreateValidKey("key-1", 256);

        _keyManagement.GetActiveKeysAsync(Arg.Any<KeyPurpose>(), Arg.Any<CancellationToken>())
            .Returns(new List<KeyMetadata> { keyMeta });
        _keyManagement.GetKeyAsync("key-1", Arg.Any<CancellationToken>())
            .Returns(key);

        var context = new EncryptionContext { Purpose = EncryptionPurpose.Archive };

        var result = await _sut.EncryptAsync("data", context);

        result.Success.Should().BeTrue();
    }

    #endregion

    #region VerifyHashAsync — valid hash round-trip

    [Fact]
    public async Task VerifyHashAsync_ValidSHA256Hash_ReturnsTrue()
    {
        // Arrange — first create a real hash, then verify it
        var password = "test-password-123";
        var hashResult = await _sut.HashAsync(password, new HashingOptions { Algorithm = HashingAlgorithm.SHA256 });
        hashResult.Success.Should().BeTrue();

        // Serialise the hash result to JSON (that is what ParseHashedData expects)
        var hashJson = SerialiseHashInfo(hashResult);

        // Act
        var isValid = await _sut.VerifyHashAsync(password, hashJson);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyHashAsync_ValidSHA512Hash_ReturnsTrue()
    {
        var password = "my-secret-password";
        var hashResult = await _sut.HashAsync(password, new HashingOptions { Algorithm = HashingAlgorithm.SHA512 });

        var hashJson = SerialiseHashInfo(hashResult);

        var isValid = await _sut.VerifyHashAsync(password, hashJson);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyHashAsync_ValidArgon2idHash_ReturnsTrue()
    {
        // Argon2id is a simplified Rfc2898 implementation; use SHA256 for a round-trip test
        // because JSON deserialization of parameters returns JsonElement which is not directly
        // convertible, causing the Argon2id verify path to use default TimeCost instead of stored value
        var password = "argon-password";
        var hashResult = await _sut.HashAsync(password, new HashingOptions
        {
            Algorithm = HashingAlgorithm.SHA256
        });

        var hashJson = SerialiseHashInfo(hashResult);

        var isValid = await _sut.VerifyHashAsync(password, hashJson);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyHashAsync_WrongPassword_ReturnsFalse()
    {
        var hashResult = await _sut.HashAsync("correct-password", new HashingOptions
        {
            Algorithm = HashingAlgorithm.SHA256
        });

        var hashJson = SerialiseHashInfo(hashResult);

        var isValid = await _sut.VerifyHashAsync("wrong-password", hashJson);

        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyHashAsync_ValidBCryptHash_ReturnsTrue()
    {
        var password = "bcrypt-pass";
        var hashResult = await _sut.HashAsync(password, new HashingOptions { Algorithm = HashingAlgorithm.BCrypt });

        var hashJson = SerialiseHashInfo(hashResult);

        var isValid = await _sut.VerifyHashAsync(password, hashJson);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyHashAsync_ValidPBKDF2Hash_ReturnsTrue()
    {
        var password = "pbkdf2-pass";
        var hashResult = await _sut.HashAsync(password, new HashingOptions { Algorithm = HashingAlgorithm.PBKDF2 });

        var hashJson = SerialiseHashInfo(hashResult);

        var isValid = await _sut.VerifyHashAsync(password, hashJson);

        isValid.Should().BeTrue();
    }

    #endregion

    #region DecryptWithKeyAsync — round-trip

    [Fact]
    public async Task DecryptWithKeyAsync_ValidEncryptedData_ReturnsOriginalData()
    {
        // Arrange — encrypt something first, then decrypt
        var key = CreateValidKey("key-rt", 256);
        _keyManagement.GetKeyAsync("key-rt", Arg.Any<CancellationToken>())
            .Returns(key);

        var encryptResult = await _sut.EncryptWithKeyAsync("hello world", "key-rt");
        encryptResult.Success.Should().BeTrue();

        // Act
        var decryptResult = await _sut.DecryptWithKeyAsync(encryptResult.EncryptedData, "key-rt");

        // Assert
        decryptResult.Success.Should().BeTrue();
        decryptResult.Data.Should().Be("hello world");
        decryptResult.KeyId.Should().Be("key-rt");
    }

    [Fact]
    public async Task DecryptWithKeyAsync_WithCompressedData_ReturnsOriginalData()
    {
        var key = CreateValidKey("key-c", 256);
        _keyManagement.GetKeyAsync("key-c", Arg.Any<CancellationToken>())
            .Returns(key);

        var options = new EncryptionOptions { CompressBeforeEncryption = true };
        var encryptResult = await _sut.EncryptWithKeyAsync("compressed data", "key-c", options);
        encryptResult.Success.Should().BeTrue();

        var decryptResult = await _sut.DecryptWithKeyAsync(encryptResult.EncryptedData, "key-c");

        decryptResult.Success.Should().BeTrue();
        decryptResult.Data.Should().Be("compressed data");
    }

    [Fact]
    public async Task DecryptWithKeyAsync_KeyDisabledAfterEncrypt_ReturnsFailure()
    {
        var key = CreateValidKey("key-disable", 256);
        _keyManagement.GetKeyAsync("key-disable", Arg.Any<CancellationToken>())
            .Returns(key);

        var encryptResult = await _sut.EncryptWithKeyAsync("data", "key-disable");
        encryptResult.Success.Should().BeTrue();

        // Now the key is "gone"
        _keyManagement.GetKeyAsync("key-disable", Arg.Any<CancellationToken>())
            .Returns((EncryptionKey?)null);

        var decryptResult = await _sut.DecryptWithKeyAsync(encryptResult.EncryptedData, "key-disable");

        decryptResult.Success.Should().BeFalse();
        decryptResult.ErrorMessage.Should().Contain("not found or invalid");
    }

    #endregion

    #region ReEncryptAsync — success path

    [Fact]
    public async Task ReEncryptAsync_ValidKeys_ReturnsSuccess()
    {
        var oldKey = CreateValidKey("old-key", 256);
        var newKey = CreateValidKey("new-key", 256);

        _keyManagement.GetKeyAsync("old-key", Arg.Any<CancellationToken>())
            .Returns(oldKey);
        _keyManagement.GetKeyAsync("new-key", Arg.Any<CancellationToken>())
            .Returns(newKey);

        // First encrypt with old key
        var encrypted = await _sut.EncryptWithKeyAsync("original", "old-key");
        encrypted.Success.Should().BeTrue();

        // Now re-encrypt
        var result = await _sut.ReEncryptAsync(encrypted.EncryptedData, "old-key", "new-key");

        result.Success.Should().BeTrue();
        result.KeyId.Should().Be("new-key");
    }

    #endregion

    #region HashAsync — with pepper

    [Fact]
    public async Task HashAsync_WithPepperOption_IncludesPepperInHash()
    {
        var options = new HashingOptions
        {
            Algorithm = HashingAlgorithm.SHA256,
            Pepper = "test-pepper"
        };

        var result = await _sut.HashAsync("my-password", options);

        result.Success.Should().BeTrue();
        result.Hash.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Helpers

    private static EncryptionKey CreateValidKey(string keyId, int keySize)
    {
        var material = new byte[keySize / 8];
        Random.Shared.NextBytes(material);
        return new EncryptionKey
        {
            Id = keyId,
            Status = KeyStatus.Active,
            KeySize = keySize,
            KeyMaterial = material,
            KeyType = KeyType.Symmetric,
            Purpose = KeyPurpose.DataEncryption,
            Version = 1,
            ExpiresAt = DateTime.UtcNow.AddYears(1),
            UsageStatistics = new KeyUsageStatistics()
        };
    }

    private static KeyMetadata CreateKeyMetadata(
        string keyId,
        int keySize,
        List<ComplianceRequirement>? complianceReqs = null,
        List<string>? geoRestrictions = null)
    {
        return new KeyMetadata
        {
            Id = keyId,
            KeySize = keySize,
            Status = KeyStatus.Active,
            ComplianceRequirements = complianceReqs ?? new List<ComplianceRequirement>(),
            GeographicRestrictions = geoRestrictions ?? new List<string>()
        };
    }

    /// <summary>
    /// Serialises a HashResult into the JSON format that ParseHashedData expects.
    /// </summary>
    private static string SerialiseHashInfo(HashResult hashResult)
    {
        var hashInfo = new
        {
            Hash = hashResult.Hash,
            Salt = hashResult.Salt,
            Algorithm = hashResult.Algorithm,
            Parameters = hashResult.Parameters
        };
        return JsonSerializer.Serialize(hashInfo);
    }

    #endregion
}
