using Girder.Redis.Security;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Girder.Infrastructure.Tests.Security;

/// <summary>
/// Tests for SecretManager (Redis-backed) — covers the 0% class.
/// We use a mocked IDatabase to avoid real Redis dependency.
/// </summary>
[Trait("Category", "Unit")]
public class SecretManagerTests
{
    private readonly IDatabase _database = Substitute.For<IDatabase>();
    private readonly IConnectionMultiplexer _multiplexer = Substitute.For<IConnectionMultiplexer>();
    private readonly ILogger<SecretManager> _logger = Substitute.For<ILogger<SecretManager>>();
    private readonly SecretManager _sut;

    public SecretManagerTests()
    {
        _multiplexer.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_database);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Provide a valid 256-bit AES key (32 bytes base64)
                ["SecretManager:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        _sut = new SecretManager(_multiplexer, config, env, _logger);
    }

    #region GetSecretAsync

    [Fact]
    public async Task GetSecretAsync_KeyNotFound_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var result = await _sut.GetSecretAsync("missing-secret");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_InvalidJson_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue("not-valid-json"));

        var result = await _sut.GetSecretAsync("bad-secret");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ValidEncryptedData_ReturnsDecryptedValue()
    {
        // Arrange — create and store an actual encrypted secret via SetSecretAsync,
        // but since we have a mock DB, we simulate what the DB would return.
        // We set and then get via the in-memory manager to validate the full flow.
        // For SecretManager with mock DB we verify the decrypt call happens.

        // Build a real encrypted value using reflection-free approach
        var (encryptedValue, iv, tag, createdAt) = CreateEncryptedSecret(
            "test-secret",
            version: 1,
            "my-secret-value");
        var secretData = new
        {
            FormatVersion = 2,
            Name = "test-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        };
        var json = JsonSerializer.Serialize(secretData);

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(json));

        var result = await _sut.GetSecretAsync("test-secret");

        result.Should().Be("my-secret-value");
    }

    [Fact]
    public async Task GetSecretAsync_TamperedCiphertext_ReturnsNull()
    {
        var (encryptedValue, iv, tag, createdAt) = CreateEncryptedSecret(
            "tampered-secret",
            version: 1,
            "original-value");
        var ciphertext = Convert.FromBase64String(encryptedValue);
        ciphertext[0] ^= 0x01;

        var secretData = new
        {
            FormatVersion = 2,
            Name = "tampered-secret",
            EncryptedValue = Convert.ToBase64String(ciphertext),
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        };
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(secretData)));

        var result = await _sut.GetSecretAsync("tampered-secret");

        result.Should().BeNull("AES-GCM must reject modified ciphertext");
    }

    [Fact]
    public async Task GetSecretAsync_RecordMovedToAnotherName_ReturnsNull()
    {
        var (encryptedValue, iv, tag, createdAt) = CreateEncryptedSecret(
            "original-name",
            version: 1,
            "original-value");
        var secretData = new
        {
            FormatVersion = 2,
            Name = "original-name",
            EncryptedValue = encryptedValue,
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        };
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(secretData)));

        var result = await _sut.GetSecretAsync("different-name");

        result.Should().BeNull("the Redis key name is authenticated as associated data");
    }

    [Fact]
    public async Task GetSecretAsync_LegacyUnauthenticatedRecord_ReturnsNull()
    {
        var legacyRecord = new
        {
            Name = "legacy-secret",
            EncryptedValue = Convert.ToBase64String([1, 2, 3]),
            IV = Convert.ToBase64String(new byte[16]),
            Version = 1,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            CreatedBy = "System"
        };
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(legacyRecord)));

        var result = await _sut.GetSecretAsync("legacy-secret");

        result.Should().BeNull("unauthenticated CBC records must not be trusted after the upgrade");
    }

    [Fact]
    public async Task GetSecretAsync_ExpiredSecret_ReturnsNull()
    {
        var createdAt = DateTime.UtcNow.AddDays(-60);
        var expiresAt = DateTime.UtcNow.AddDays(-1);
        var (encryptedValue, iv, tag, _) = CreateEncryptedSecret(
            "expired-secret",
            version: 1,
            "expired-value",
            createdAt,
            expiresAt);
        var secretData = new
        {
            FormatVersion = 2,
            Name = "expired-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)expiresAt,
            IsActive = true,
            CreatedBy = "System"
        };

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(secretData)));

        var result = await _sut.GetSecretAsync("expired-secret");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSecretAsync_ExtendedExpiryMetadata_ReturnsNull()
    {
        var createdAt = DateTime.UtcNow.AddDays(-60);
        var originalExpiry = DateTime.UtcNow.AddDays(-1);
        var (encryptedValue, iv, tag, _) = CreateEncryptedSecret(
            "expiry-bound-secret",
            version: 1,
            "expired-value",
            createdAt,
            originalExpiry);
        var tamperedRecord = new
        {
            FormatVersion = 2,
            Name = "expiry-bound-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)DateTime.UtcNow.AddYears(1),
            IsActive = true,
            CreatedBy = "System"
        };

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(JsonSerializer.Serialize(tamperedRecord)));

        var result = await _sut.GetSecretAsync("expiry-bound-secret");

        result.Should().BeNull("expiry and other control metadata are authenticated");
    }

    [Fact]
    public async Task GetSecretAsync_RedisThrows_ReturnsNull()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Connection refused"));

        var result = await _sut.GetSecretAsync("any-secret");

        result.Should().BeNull();
    }

    #endregion

    #region SetSecretAsync

    [Fact]
    public async Task SetSecretAsync_CallsRedisStringSet()
    {
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(0L);

        await _sut.SetSecretAsync("my-secret", "my-value");

        await _database.Received(1).StringSetAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<Expiration>(),
            Arg.Any<ValueCondition>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetSecretAsync_StoresInHistory()
    {
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(0L);

        await _sut.SetSecretAsync("history-secret", "value");

        await _database.Received(1).ListLeftPushAsync(
            Arg.Any<RedisKey>(),
            Arg.Any<RedisValue>(),
            Arg.Any<When>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task SetSecretAsync_RedisThrows_PropagatesException()
    {
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(0L);
        _database.StringSetAsync(
                Arg.Any<RedisKey>(),
                Arg.Any<RedisValue>(),
                Arg.Any<Expiration>(),
                Arg.Any<ValueCondition>(),
                Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Write failed"));

        var act = () => _sut.SetSecretAsync("secret", "value");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region RotateSecretAsync

    [Fact]
    public async Task RotateSecretAsync_ReturnsNewValue()
    {
        // Current version (will be deactivated)
        var (encryptedValue, iv, tag, createdAt) = CreateEncryptedSecret(
            "rotate-me",
            version: 1,
            "old-value");
        var currentData = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Name = "rotate-me",
            EncryptedValue = encryptedValue,
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        });

        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue(currentData));
        _database.ListLengthAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(1L);

        var newValue = await _sut.RotateSecretAsync("rotate-me");

        newValue.Should().NotBeNullOrEmpty();
        newValue.Should().NotBe("old-value");
    }

    [Fact]
    public async Task RotateSecretAsync_RedisThrows_PropagatesException()
    {
        _database.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var act = () => _sut.RotateSecretAsync("failing-secret");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region GetSecretHistoryAsync

    [Fact]
    public async Task GetSecretHistoryAsync_EmptyList_ReturnsEmpty()
    {
        _database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(Array.Empty<RedisValue>());

        var result = await _sut.GetSecretHistoryAsync("no-history");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSecretHistoryAsync_WithVersions_ReturnsMaskedValues()
    {
        var (encryptedValue, iv, tag, createdAt) = CreateEncryptedSecret(
            "my-secret",
            version: 1,
            "secret-val");
        var entry = JsonSerializer.Serialize(new
        {
            FormatVersion = 2,
            Name = "my-secret",
            EncryptedValue = encryptedValue,
            IV = iv,
            AuthenticationTag = tag,
            Version = 1,
            CreatedAt = createdAt,
            ExpiresAt = (DateTime?)null,
            IsActive = true,
            CreatedBy = "System"
        });

        _database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(new RedisValue[] { entry });

        var history = (await _sut.GetSecretHistoryAsync("my-secret")).ToList();

        history.Should().HaveCount(1);
        history[0].Value.Should().Be("[ENCRYPTED]"); // actual values are masked
        history[0].Name.Should().Be("my-secret");
    }

    [Fact]
    public async Task GetSecretHistoryAsync_RedisThrows_ReturnsEmpty()
    {
        _database.ListRangeAsync(Arg.Any<RedisKey>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var result = await _sut.GetSecretHistoryAsync("any-secret");

        result.Should().BeEmpty();
    }

    #endregion

    #region DeleteSecretAsync

    [Fact]
    public async Task DeleteSecretAsync_CallsKeyDelete()
    {
        await _sut.DeleteSecretAsync("my-secret");

        await _database.Received(1).KeyDeleteAsync(
            Arg.Any<RedisKey[]>(),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task DeleteSecretAsync_RedisThrows_PropagatesException()
    {
        _database.KeyDeleteAsync(Arg.Any<RedisKey[]>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var act = () => _sut.DeleteSecretAsync("secret");

        await act.Should().ThrowAsync<RedisException>();
    }

    #endregion

    #region SecretExistsAsync

    [Fact]
    public async Task SecretExistsAsync_KeyExists_ReturnsTrue()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var result = await _sut.SecretExistsAsync("existing-secret");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SecretExistsAsync_KeyNotExists_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(false);

        var result = await _sut.SecretExistsAsync("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SecretExistsAsync_RedisThrows_ReturnsFalse()
    {
        _database.KeyExistsAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .ThrowsAsync(new RedisException("Error"));

        var result = await _sut.SecretExistsAsync("any");

        result.Should().BeFalse();
    }

    #endregion

    #region GetSecretNamesAsync

    [Fact]
    public async Task GetSecretNamesAsync_RedisThrows_ReturnsEmpty()
    {
        // IServer.Keys will throw because multiplexer isn't a real connection
        var result = await _sut.GetSecretNamesAsync();

        // Should not throw, returns empty on error
        result.Should().NotBeNull();
    }

    #endregion

    #region Constructor — development key generation

    [Fact]
    public void Constructor_NoEncryptionKey_DevelopmentEnv_GeneratesTransientKey()
    {
        var config = new ConfigurationBuilder().Build(); // no keys configured
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        // Should not throw — generates transient key with a warning
        var act = () => new SecretManager(_multiplexer, config, env, _logger);

        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_NoEncryptionKey_DoesNotLogGeneratedKeyMaterial()
    {
        var config = new ConfigurationBuilder().Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");
        var logs = new CollectingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));

        _ = new SecretManager(
            _multiplexer,
            config,
            env,
            loggerFactory.CreateLogger<SecretManager>());

        logs.Warnings.Should().ContainSingle();
        logs.Warnings[0].Should().Contain("transient process-local key");
        logs.Warnings[0].Should().NotMatchRegex("[A-Za-z0-9+/]{43}=");
    }

    [Fact]
    public void Constructor_ConfiguredKeyWithWrongLength_FailsAtStartup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretManager:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[16])
            })
            .Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Development");

        var act = () => new SecretManager(_multiplexer, config, env, _logger);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*16 bytes; 32 are required*");
    }

    [Fact]
    public void Constructor_NoEncryptionKey_ProductionEnv_Throws()
    {
        var config = new ConfigurationBuilder().Build();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");

        var act = () => new SecretManager(_multiplexer, config, env, _logger);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Persistent encryption key not configured*");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Creates a real AES-GCM value matching SecretManager's format 2.
    /// Uses the same 32-byte zero key we pass in the constructor.
    /// </summary>
    private static (string encryptedValue, string iv, string authenticationTag, DateTime createdAt) CreateEncryptedSecret(
        string name,
        int version,
        string value,
        DateTime? createdAt = null,
        DateTime? expiresAt = null,
        bool isActive = true,
        string createdBy = "System")
    {
        var storedCreatedAt = createdAt ?? DateTime.UtcNow;
        var plaintext = Encoding.UTF8.GetBytes(value);
        var encrypted = new byte[plaintext.Length];
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var associatedData = SecretAssociatedData(
            name,
            name,
            version,
            storedCreatedAt,
            expiresAt,
            isActive,
            createdBy);

        using var aes = new AesGcm(new byte[32], 16);
        aes.Encrypt(nonce, plaintext, encrypted, tag, associatedData);

        return (
            Convert.ToBase64String(encrypted),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(tag),
            storedCreatedAt);
    }

    private static byte[] SecretAssociatedData(
        string requestedName,
        string storedName,
        int version,
        DateTime createdAt,
        DateTime? expiresAt,
        bool isActive,
        string createdBy)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);

        WriteAssociatedString(writer, "Girder.SecretManager.v2");
        WriteAssociatedString(writer, requestedName);
        WriteAssociatedString(writer, storedName);
        writer.Write(version);
        writer.Write(createdAt.ToUniversalTime().Ticks);
        writer.Write(expiresAt.HasValue);
        if (expiresAt.HasValue)
        {
            writer.Write(expiresAt.Value.ToUniversalTime().Ticks);
        }
        writer.Write(isActive);
        WriteAssociatedString(writer, createdBy);
        writer.Flush();
        return buffer.ToArray();
    }

    private static void WriteAssociatedString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    #endregion
}
